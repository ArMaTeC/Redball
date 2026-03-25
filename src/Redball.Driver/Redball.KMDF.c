/*
 * Redball.KMDF.c
 * Redball Input Filter Driver - Interception Compatibility Implementation
 */

#include "Redball.KMDF.h"

//
// Internal Types for Hooking
//
typedef struct _CONNECT_DATA {
    PDEVICE_OBJECT ClassDeviceObject;
    PVOID          ClassService;
} CONNECT_DATA, *PCONNECT_DATA;

typedef VOID
(*PSERVICE_CALLBACK_ROUTINE)(
    _In_    PDEVICE_OBJECT DeviceObject,
    _In_    PKEYBOARD_INPUT_DATA InputDataStart,
    _In_    PKEYBOARD_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
    );

typedef struct _REDBALL_DEVICE_CONTEXT {
    WDFDEVICE WdfDevice;
    WDFQUEUE  NotificationQueue;
    USHORT    KeyboardFilter;
    
    // Hooking data
    CONNECT_DATA UpperConnectData;
} REDBALL_DEVICE_CONTEXT, *PREDBALL_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(REDBALL_DEVICE_CONTEXT, GetRedballContext)

//
// Prototypes
//
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL RedballEvtIoDeviceControl;
EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL RedballEvtInternalDeviceControl;

VOID
RedballServiceCallback(
    _In_    PDEVICE_OBJECT DeviceObject,
    _In_    PKEYBOARD_INPUT_DATA InputDataStart,
    _In_    PKEYBOARD_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
);

//
// Implementation
//

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, RedballEvtDeviceAdd);
    return WdfDriverCreate(DriverObject, RegistryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}

NTSTATUS
RedballEvtDeviceAdd(
    _In_ WDFDRIVER       Driver,
    _In_ PWDFDEVICE_INIT DeviceInit
)
{
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDF_IO_QUEUE_CONFIG queueConfig;
    WDFDEVICE device;
    NTSTATUS status;
    PREDBALL_DEVICE_CONTEXT context;
    DECLARE_UNICODE_STRING_SIZE(symbolicLinkName, 64);

    UNREFERENCED_PARAMETER(Driver);
    WdfFdoInitSetFilter(DeviceInit);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, REDBALL_DEVICE_CONTEXT);

    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) return status;

    context = GetRedballContext(device);
    context->WdfDevice = device;
    context->KeyboardFilter = 0;

    // Default Control Queue
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoDeviceControl = RedballEvtIoDeviceControl;
    queueConfig.EvtIoInternalDeviceControl = RedballEvtInternalDeviceControl;

    status = WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &context->NotificationQueue);
    if (!NT_SUCCESS(status)) return status;

    RtlUnicodeStringPrintf(&symbolicLinkName, L"\\DosDevices\\interception00");
    WdfDeviceCreateSymbolicLink(device, &symbolicLinkName);

    return STATUS_SUCCESS;
}

VOID
RedballEvtInternalDeviceControl(
    _In_ WDFQUEUE    Queue,
    _In_ WDFREQUEST  Request,
    _In_ size_t      OutputBufferLength,
    _In_ size_t      InputBufferLength,
    _In_ ULONG       IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    PREDBALL_DEVICE_CONTEXT context = GetRedballContext(device);
    PCONNECT_DATA connectData;

    UNREFERENCED_PARAMETER(OutputBufferLength);

    if (IoControlCode == IOCTL_INTERNAL_KEYBOARD_CONNECT) {
        if (InputBufferLength < sizeof(CONNECT_DATA)) {
            status = STATUS_INVALID_PARAMETER;
        } else {
            status = WdfRequestRetrieveInputBuffer(Request, sizeof(CONNECT_DATA), &connectData, NULL);
            if (NT_SUCCESS(status)) {
                // Save the original callback
                context->UpperConnectData = *connectData;

                // Hook with ours
                connectData->ClassDeviceObject = WdfDeviceWdmGetDeviceObject(device);
                connectData->ClassService = RedballServiceCallback;
            }
        }
    }

    if (NT_SUCCESS(status)) {
        WdfRequestFormatRequestUsingCurrentType(Request);
        WdfRequestSend(Request, WdfDeviceGetIoTarget(device), WDF_NO_SEND_OPTIONS);
    } else {
        WdfRequestComplete(Request, status);
    }
}

VOID
RedballServiceCallback(
    _In_    PDEVICE_OBJECT DeviceObject,
    _In_    PKEYBOARD_INPUT_DATA InputDataStart,
    _In_    PKEYBOARD_INPUT_DATA InputDataEnd,
    _Inout_ PULONG InputDataConsumed
)
{
    WDFDEVICE device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    PREDBALL_DEVICE_CONTEXT context = GetRedballContext(device);
    PSERVICE_CALLBACK_ROUTINE originalCallback;

    // -----------------------------------------------------------------
    // PRE-CALLBACK FILTERING
    // This is where we would check the KeyboardFilter and 
    // potentially drop or modify packets.
    // -----------------------------------------------------------------

    originalCallback = (PSERVICE_CALLBACK_ROUTINE)context->UpperConnectData.ClassService;

    // Call the original kbdclass callback to pass input to the OS
    (*originalCallback)(context->UpperConnectData.ClassDeviceObject,
                         InputDataStart,
                         InputDataEnd,
                         InputDataConsumed);
}

VOID
RedballEvtIoDeviceControl(
    _In_ WDFQUEUE   Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t     OutputBufferLength,
    _In_ size_t     InputBufferLength,
    _In_ ULONG      IoControlCode
)
{
    NTSTATUS status = STATUS_SUCCESS;
    PREDBALL_DEVICE_CONTEXT context = GetRedballContext(WdfIoQueueGetDevice(Queue));
    PVOID buffer = NULL;

    switch (IoControlCode) {
        case IOCTL_SET_FILTER:
            if (InputBufferLength >= sizeof(USHORT)) {
                status = WdfRequestRetrieveInputBuffer(Request, sizeof(USHORT), &buffer, NULL);
                if (NT_SUCCESS(status)) {
                    context->KeyboardFilter = *(PUSHORT)buffer;
                }
            }
            break;

        case IOCTL_GET_FILTER:
            if (OutputBufferLength >= sizeof(USHORT)) {
                status = WdfRequestRetrieveOutputBuffer(Request, sizeof(USHORT), &buffer, NULL);
                if (NT_SUCCESS(status)) {
                    *(PUSHORT)buffer = context->KeyboardFilter;
                    WdfRequestSetInformation(Request, sizeof(USHORT));
                }
            }
            break;

        default:
            status = STATUS_INVALID_DEVICE_REQUEST;
            break;
    }

    WdfRequestComplete(Request, status);
}
