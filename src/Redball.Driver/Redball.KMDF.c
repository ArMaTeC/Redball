/*
 * Redball.KMDF.c
 * Implementation of the Redball Input Filter Driver.
 */

#include "Redball.KMDF.h"

//
// Main Entry Point
//
NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT  DriverObject,
    _In_ PUNICODE_STRING RegistryPath
)
{
    WDF_DRIVER_CONFIG config;
    NTSTATUS status;

    WDF_DRIVER_CONFIG_INIT(&config, RedballEvtDeviceAdd);

    status = WdfDriverCreate(DriverObject, RegistryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
    return status;
}

//
// Device Initialization
//
NTSTATUS
RedballEvtDeviceAdd(
    _In_ WDFDRIVER       Driver,
    _In_ PWDFDEVICE_INIT DeviceInit
)
{
    WDF_OBJECT_ATTRIBUTES deviceAttributes;
    WDFDEVICE device;
    NTSTATUS status;
    PDEVICE_CONTEXT context;

    UNREFERENCED_PARAMETER(Driver);

    // Set side-band communication capability
    WdfFdoInitSetFilter(DeviceInit);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&deviceAttributes, DEVICE_CONTEXT);

    status = WdfDeviceCreate(&DeviceInit, &deviceAttributes, &device);
    if (!NT_SUCCESS(status)) return status;

    context = GetDeviceContext(device);
    context->WdfDevice = device;
    context->IsIntercepting = TRUE;

    return STATUS_SUCCESS;
}

//
// I/O Control Handler
//
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
    
    UNREFERENCED_PARAMETER(OutputBufferLength);
    UNREFERENCED_PARAMETER(InputBufferLength);

    switch (IoControlCode) {
        // Here we would intercept the KEYBOARD_INPUT_DATA via the 
        // IOCTL_INTERNAL_KEYBOARD_CONNECT hook.
        // For the absolute basic version, we just pass through.
        default:
            break;
    }

    WdfRequestFormatRequestUsingCurrentType(Request);
    WdfRequestSend(Request, WdfDeviceGetIoTarget(device), WDF_NO_SEND_OPTIONS);
}
