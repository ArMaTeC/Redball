/*
 * Redball.KMDF.h
 * Redball Input Filter Driver - Interception Compatibility Layer
 */

#ifndef _REDBALL_KMDF_H_
#define _REDBALL_KMDF_H_

#include <ntddk.h>
#include <wdf.h>
#include <kbdmou.h>

// Legacy Interception IOCTLs
#define IOCTL_SET_PRECEDENCE    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_GET_PRECEDENCE    CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_SET_FILTER        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x804, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_GET_FILTER        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x808, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_SET_EVENT         CTL_CODE(FILE_DEVICE_UNKNOWN, 0x810, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_WRITE             CTL_CODE(FILE_DEVICE_UNKNOWN, 0x820, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_READ              CTL_CODE(FILE_DEVICE_UNKNOWN, 0x840, METHOD_BUFFERED, FILE_ANY_ACCESS)
#define IOCTL_GET_HARDWARE_ID   CTL_CODE(FILE_DEVICE_UNKNOWN, 0x880, METHOD_BUFFERED, FILE_ANY_ACCESS)

typedef struct _INTERCEPTION_KEY_STROKE
{
    unsigned short code;
    unsigned short state;
    unsigned int information;
} INTERCEPTION_KEY_STROKE, *PINTERCEPTION_KEY_STROKE;

typedef struct _INTERCEPTION_MOUSE_STROKE
{
    unsigned short state;
    unsigned short flags;
    short rolling;
    int x;
    int y;
    unsigned int information;
} INTERCEPTION_MOUSE_STROKE, *PINTERCEPTION_MOUSE_STROKE;

// typedef VOID (*PSERVICE_CALLBACK_ROUTINE)(PDEVICE_OBJECT, PVOID, PVOID, PULONG);

// Driver Context
typedef struct _REDBALL_DEVICE_CONTEXT {
    WDFDEVICE WdfDevice;
    WDFQUEUE  ControlQueue;        // For IOCTLs
    WDFQUEUE  StrokeQueue;         // For intercepted strokes
    USHORT    KeyboardFilter;
    USHORT    MouseFilter;
    
    // Hooking data
    CONNECT_DATA UpperConnectData;
} REDBALL_DEVICE_CONTEXT, *PREDBALL_DEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(REDBALL_DEVICE_CONTEXT, GetRedballContext)

// Prototypes
NTSTATUS DriverEntry(PDRIVER_OBJECT DriverObject, PUNICODE_STRING RegistryPath);
NTSTATUS RedballEvtDeviceAdd(WDFDRIVER Driver, PWDFDEVICE_INIT DeviceInit);

#endif // _REDBALL_KMDF_H_
