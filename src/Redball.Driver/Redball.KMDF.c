/*
 * Redball.KMDF.c
 * Redball Input Filter Driver - Interception Compatibility Implementation
 */

#include "Redball.KMDF.h"

//
// Internal Types for Hooking
//
#include <ntstrsafe.h>

// Types and function pointers are in Redball.KMDF.h

// Prototypes
VOID RedballServiceCallback(PDEVICE_OBJECT DeviceObject, PVOID InputDataStart, PVOID InputDataEnd, PULONG InputDataConsumed);
VOID RedballMouseServiceCallback(PDEVICE_OBJECT DeviceObject, PVOID InputDataStart, PVOID InputDataEnd, PULONG InputDataConsumed);

EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL RedballEvtIoDeviceControl;
EVT_WDF_IO_QUEUE_IO_INTERNAL_DEVICE_CONTROL RedballEvtInternalDeviceControl;

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
    UNICODE_STRING symbolicLinkName;

    UNREFERENCED_PARAMETER(Driver);
    WdfFdoInitSetFilter(DeviceInit);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, REDBALL_DEVICE_CONTEXT);

    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) return status;

    context = GetRedballContext(device);
    context->WdfDevice = device;
    context->KeyboardFilter = 0;
    context->MouseFilter = 0;

    // Default Control Queue (Parallel)
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queueConfig, WdfIoQueueDispatchParallel);
    queueConfig.EvtIoDeviceControl = RedballEvtIoDeviceControl;
    queueConfig.EvtIoInternalDeviceControl = RedballEvtInternalDeviceControl;

    status = WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &context->ControlQueue);
    if (!NT_SUCCESS(status)) return status;

    // Manual Stroke Queue
    WDF_IO_QUEUE_CONFIG_INIT(&queueConfig, WdfIoQueueDispatchManual);
    status = WdfIoQueueCreate(device, &queueConfig, WDF_NO_OBJECT_ATTRIBUTES, &context->StrokeQueue);
    if (!NT_SUCCESS(status)) return status;

    RtlInitUnicodeString(&symbolicLinkName, L"\\DosDevices\\interception01");
    WdfDeviceCreateSymbolicLink(device, &symbolicLinkName);

    return STATUS_SUCCESS;
}

// Prototypes already declared above

// ... (in RedballEvtDeviceAdd, initialize MouseFilter = 0)

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
    UNREFERENCED_PARAMETER(InputBufferLength);

    if (IoControlCode == IOCTL_INTERNAL_KEYBOARD_CONNECT) {
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(CONNECT_DATA), &connectData, NULL);
        if (NT_SUCCESS(status)) {
            context->UpperConnectData = *connectData;
            connectData->ClassDeviceObject = WdfDeviceWdmGetDeviceObject(device);
            connectData->ClassService = (PVOID)RedballServiceCallback;
        }
    } else if (IoControlCode == IOCTL_INTERNAL_MOUSE_CONNECT) {
        status = WdfRequestRetrieveInputBuffer(Request, sizeof(CONNECT_DATA), &connectData, NULL);
        if (NT_SUCCESS(status)) {
            // Note: We use the same UpperConnectData slot for simplicity in this draft, 
            // but a real driver would have separate slots for Kbd/Mou.
            context->UpperConnectData = *connectData;
            connectData->ClassDeviceObject = WdfDeviceWdmGetDeviceObject(device);
            connectData->ClassService = (PVOID)RedballMouseServiceCallback;
        }
    }

    if (NT_SUCCESS(status)) {
        WdfRequestFormatRequestUsingCurrentType(Request);
        WdfRequestSend(Request, WdfDeviceGetIoTarget(device), WDF_NO_SEND_OPTIONS);
    } else {
        WdfRequestComplete(Request, status);
    }
}

VOID RedballServiceCallback(PDEVICE_OBJECT DeviceObject, PVOID InputDataStart, PVOID InputDataEnd, PULONG InputDataConsumed)
{
    PKEYBOARD_INPUT_DATA kbdStart = (PKEYBOARD_INPUT_DATA)InputDataStart;
    PKEYBOARD_INPUT_DATA kbdEnd = (PKEYBOARD_INPUT_DATA)InputDataEnd;
    WDFDEVICE device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    PREDBALL_DEVICE_CONTEXT context = GetRedballContext(device);
    PSERVICE_CALLBACK_ROUTINE originalCallback = (PSERVICE_CALLBACK_ROUTINE)context->UpperConnectData.ClassService;
    WDFREQUEST request;
    PINTERCEPTION_KEY_STROKE userStroke;
    BOOLEAN dropPacket = FALSE;

    if (context->KeyboardFilter != 0) {
        if (NT_SUCCESS(WdfIoQueueRetrieveNextRequest(context->StrokeQueue, &request))) {
            if (NT_SUCCESS(WdfRequestRetrieveOutputBuffer(request, sizeof(INTERCEPTION_KEY_STROKE), &userStroke, NULL))) {
                userStroke->code = kbdStart->MakeCode;
                userStroke->state = kbdStart->Flags;
                userStroke->information = kbdStart->ExtraInformation;
                WdfRequestCompleteWithInformation(request, STATUS_SUCCESS, sizeof(INTERCEPTION_KEY_STROKE));
                if (context->KeyboardFilter & 0x01) dropPacket = TRUE;
            } else {
                WdfRequestComplete(request, STATUS_INTERNAL_ERROR);
            }
        }
    }

    if (!dropPacket) {
        (*originalCallback)(context->UpperConnectData.ClassDeviceObject, kbdStart, kbdEnd, InputDataConsumed);
    } else {
        *InputDataConsumed = (ULONG)(kbdEnd - kbdStart);
    }
}

VOID RedballMouseServiceCallback(PDEVICE_OBJECT DeviceObject, PVOID InputDataStart, PVOID InputDataEnd, PULONG InputDataConsumed)
{
    PMOUSE_INPUT_DATA mouStart = (PMOUSE_INPUT_DATA)InputDataStart;
    PMOUSE_INPUT_DATA mouEnd = (PMOUSE_INPUT_DATA)InputDataEnd;
    WDFDEVICE device = WdfWdmDeviceGetWdfDeviceHandle(DeviceObject);
    PREDBALL_DEVICE_CONTEXT context = GetRedballContext(device);
    PSERVICE_CALLBACK_ROUTINE originalCallback = (PSERVICE_CALLBACK_ROUTINE)context->UpperConnectData.ClassService;
    WDFREQUEST request;
    PINTERCEPTION_MOUSE_STROKE userStroke;
    BOOLEAN dropPacket = FALSE;

    if (context->MouseFilter != 0) {
        if (NT_SUCCESS(WdfIoQueueRetrieveNextRequest(context->StrokeQueue, &request))) {
            if (NT_SUCCESS(WdfRequestRetrieveOutputBuffer(request, sizeof(INTERCEPTION_MOUSE_STROKE), &userStroke, NULL))) {
                userStroke->state = mouStart->ButtonFlags;
                userStroke->flags = mouStart->Flags;
                userStroke->rolling = mouStart->ButtonData;
                userStroke->x = mouStart->LastX;
                userStroke->y = mouStart->LastY;
                userStroke->information = mouStart->ExtraInformation;
                WdfRequestCompleteWithInformation(request, STATUS_SUCCESS, sizeof(INTERCEPTION_MOUSE_STROKE));
                if (context->MouseFilter & 0x01) dropPacket = TRUE;
            } else {
                WdfRequestComplete(request, STATUS_INTERNAL_ERROR);
            }
        }
    }

    if (!dropPacket) {
        // Cast to PSERVICE_CALLBACK_ROUTINE for mouse packets is safe as the signature is symmetric for start/end/consumed
        ((VOID (*)(PDEVICE_OBJECT, PVOID, PVOID, PULONG))originalCallback)(
            context->UpperConnectData.ClassDeviceObject, mouStart, mouEnd, InputDataConsumed);
    } else {
        *InputDataConsumed = (ULONG)(mouEnd - mouStart);
    }
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
    ULONG consumed = 0;

    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    switch (IoControlCode) {
        case IOCTL_SET_FILTER:
            if (InputBufferLength >= sizeof(USHORT)) {
                if (NT_SUCCESS(WdfRequestRetrieveInputBuffer(Request, sizeof(USHORT), &buffer, NULL))) {
                    context->KeyboardFilter = *(PUSHORT)buffer;
                    context->MouseFilter = *(PUSHORT)buffer; // Sync both for now
                }
            }
            WdfRequestComplete(Request, status);
            break;

        case IOCTL_READ:
            status = WdfRequestForwardToIoQueue(Request, context->StrokeQueue);
            if (!NT_SUCCESS(status)) WdfRequestComplete(Request, status);
            break;

        case IOCTL_WRITE:
            // Synthetic Input Injection
            if (InputBufferLength >= sizeof(KEYBOARD_INPUT_DATA)) {
                if (NT_SUCCESS(WdfRequestRetrieveInputBuffer(Request, sizeof(KEYBOARD_INPUT_DATA), &buffer, NULL))) {
                    PSERVICE_CALLBACK_ROUTINE cb = (PSERVICE_CALLBACK_ROUTINE)context->UpperConnectData.ClassService;
                    if (cb) {
                        (*cb)(context->UpperConnectData.ClassDeviceObject, (PKEYBOARD_INPUT_DATA)buffer, (PKEYBOARD_INPUT_DATA)buffer + 1, &consumed);
                    }
                }
            }
            WdfRequestComplete(Request, STATUS_SUCCESS);
            break;

        default:
            WdfRequestComplete(Request, STATUS_INVALID_DEVICE_REQUEST);
            break;
    }
}
