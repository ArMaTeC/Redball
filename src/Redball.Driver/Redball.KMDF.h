/*
 * Redball.KMDF.h
 * Core definitions for the Redball Input Filter Driver.
 */

#ifndef _REDBALL_KMDF_H_
#define _REDBALL_KMDF_H_

#include <ntddk.h>
#include <wdf.h>
#include <kbdmou.h>

// Custom IOCTLs for Redball communication
#define IOCTL_REDBALL_GET_BUFFER CTL_CODE(FILE_DEVICE_KEYBOARD, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_REDBALL_SET_STATE  CTL_CODE(FILE_DEVICE_KEYBOARD, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)

typedef struct _DEVICE_CONTEXT {
    WDFDEVICE WdfDevice;
    WDFQUEUE  NotificationQueue;
    BOOLEAN   IsIntercepting;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetDeviceContext)

// Prototypes
NTSTATUS DriverEntry(PDRIVER_OBJECT DriverObject, PUNICODE_STRING RegistryPath);
NTSTATUS RedballEvtDeviceAdd(WDFDRIVER Driver, PWDFDEVICE_INIT DeviceInit);
VOID     RedballEvtInternalDeviceControl(WDFQUEUE Queue, WDFREQUEST Request, size_t OutputBufferLength, size_t InputBufferLength, ULONG IoControlCode);

#endif // _REDBALL_KMDF_H_
