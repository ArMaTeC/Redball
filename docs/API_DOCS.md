# Redball API Documentation

This document describes the API endpoints provided by the `update-server`, as well as the local REST API exposed by the Redball WPF application.

## Update Server API

The Update Server acts as the central hub for securely managing and dispatching payloads.

### `GET /api/v1/update/check`

Checks for the latest application updates.

**Parameters:**
- `version` (string, required): The current build semantic version of the installed application.
- `channel` (string, optional): Release channel (e.g., `stable`, `beta`). Default is `stable`.

**Response (200 OK):**
```json
{
  "updateAvailable": true,
  "latestVersion": "v3.6.0",
  "downloadUrl": "url/to/download.msi",
  "releaseNotes": "Summary of changes.",
  "mandatory": false,
  "checksum": "sha256-hash"
}
```

### `POST /api/v1/admin/release`

Triggers a secure release broadcast via the Web Admin backend.

**Headers:**
- `Authorization`: Bearer `<admin-token>`

**Body:**
```json
{
  "version": "v3.6.0",
  "artifactUrl": "s3://bucket/Redball-v3.6.0.msi",
  "checksum": "sha256-hash",
  "notes": "Added Playwright E2E coverage."
}
```

**Response (201 Created):**
```json
{
  "status": "success",
  "message": "Release v3.6.0 published successfully."
}
```

## Local Client API

If `WebApiService` is enabled in `Redball.json`, the local system receives REST boundaries for script automation.

### `POST /api/local/typething/start`

Initiates the `TypeThing` sequence natively via HTTP.

**Body:**
```json
{
  "text": "Payload to type",
  "delayMs": 50
}
```

### `POST /api/local/keepawake/toggle`

Toggles the `Keep-Awake` system monitor.

**Response (200 OK):**
```json
{
  "state": "active",
  "durationRemaining": 3599
}
```
