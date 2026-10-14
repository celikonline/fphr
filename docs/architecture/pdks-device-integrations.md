# PDKS device event bridge

The Attendance API accepts vendor event payloads at `POST /api/v1/attendance/terminals/integrations/{provider}`. This is the cloud-side bridge endpoint: a local connector or vendor platform reads/receives the native device protocol and forwards the original event payload over HTTPS with the registered terminal's `X-Terminal-Key`. The endpoint does not expose device credentials or accept biometric templates/images.

Supported providers:

- `zkteco`: tab-delimited Attendance PUSH/ADMS `ATTLOG` lines (`PIN`, device-local timestamp, state, ...).
- `hikvision`: XML access-event records containing `employeeNoString` (or `employeeNo`) and `time` (or `dateTime`). `attendanceStatus` maps `checkIn`/`checkOut` to direction.
- `suprema`: BioStar JSON rows under `events`, `records`, `logs`, or `data`, with user id, event time, and optional T&A direction fields.

## Employee identity mapping

Create an `ExternalId` terminal credential for each device user. The credential value must be:

```text
{provider}:{terminal-code}:{device-user-id}
```

For example, if the terminal code is `ZK-LOBBY-01` and the ZKTeco PIN is `0042`, assign `zkteco:ZK-LOBBY-01:0042` to that employee. The API hashes this value through the existing terminal-credential pepper before lookup; the raw value is not persisted in the punch record.

The connector must use the terminal's API key as `X-Terminal-Key`. The terminal code is also the namespace for the device user id, so it should identify one physical device consistently. The body may contain multiple events, up to the existing 500-event batch limit. Timestamps with an explicit UTC offset are preferred; timestamps without one are interpreted in the tenant's configured time zone. Ambiguous daylight-saving timestamps use the earlier UTC instant, while nonexistent wall-clock timestamps are rejected.

## Example payloads

ZKTeco (`text/plain`):

```text
0042\t2026-10-14 08:55:00\t0\t1\t0
0042\t2026-10-14 17:04:00\t1\t1\t0
```

Hikvision (`application/xml`):

```xml
<AcsEvent>
  <employeeNoString>0042</employeeNoString>
  <time>2026-10-14T08:55:00+03:00</time>
  <attendanceStatus>checkIn</attendanceStatus>
  <serialNo>18724</serialNo>
</AcsEvent>
```

Suprema (`application/json`):

```json
{
  "records": [
    { "id": "18724", "user_id": "0042", "datetime": "2026-10-14T08:55:00+03:00", "tna_key": "in" }
  ]
}
```

All records pass through the existing terminal credential resolution, active-employee validation, rate limiter, batch result contract, and `IPunchRecorder`. Vendor events are persisted with the `DeviceIntegration` source and a provider-qualified source reference when the vendor supplies an event id.

## Vendor compatibility

Device firmware and payload shapes vary. ZKTeco rows that are not tab-delimited ATTLOG, Hikvision XML variants with a different event schema, or BioStar API response versions with different property names need an adapter extension and a hardware/API sample from that deployment. The endpoint is intentionally a payload bridge; it does not itself poll Hikvision ISAPI or BioStar, nor does it implement the ZKTeco ADMS handshake. Those vendor-native connections belong in the site connector that forwards payloads using the terminal key.
