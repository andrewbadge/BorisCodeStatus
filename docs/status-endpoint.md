# The `/status` endpoint

[← Back to the README](../README.md)


```
GET http://<host>:5080/status
GET http://<host>:5080/health
```

Bound to `0.0.0.0` so the ESP32 can reach it across the LAN. Port is overridable with the
`BORISCODESTATUS_PORT` environment variable. CORS allows all origins.

```json
{
  "session":  { "used_percentage": 42,    "resets_at": "2026-09-13T19:00:00+00:00", "resets_in_minutes": 656 },
  "week":     { "used_percentage": 61.25, "resets_at": "2026-09-17T04:30:00+00:00", "resets_in_minutes": 5546 },
  "week_sonnet": null,
  "context_used_percentage": 37.5,
  "model_display_name": "Opus 5",
  "session_id": "abc-123",
  "session_name": "boris code status",
  "session_cost_usd": 1.2345,
  "session_duration_ms": 843000,
  "month_cost_usd": null,
  "activity": "Working",
  "activity_changed_utc": "2026-09-13T08:03:43+00:00",
  "waiting_message": null,
  "session_status": "Active",
  "last_event_utc": "2026-09-13T08:03:43+00:00",
  "session_ended_utc": null,
  "last_updated_utc": "2026-09-13T08:03:43+00:00",
  "usage_api_last_success_utc": null,
  "age_seconds": 0
}
```

`resets_in_minutes`, `age_seconds` and `session_status` are computed per request, so the firmware
does not need a clock or timezone handling. `age_seconds` lets the display grey out stale data.

`activity` is "what Claude is doing this turn"; `session_status` is "is a session open" — see
[Session hooks](how-it-works.md#2b-session-hooks--is-a-session-open-at-all-session_status). `last_event_utc`
differs from `last_updated_utc`, which also moves for background writes such as the usage-API
refresh; only `last_event_utc` tracks actual session events.

### ⚠️ Security: the endpoint is unauthenticated

`/status` exposes session cost, usage percentages and session names to **anything on the LAN**, with
no authentication. This is a deliberate v1 decision for a home or small-office network — the ESP32
is not a browser and has nowhere to keep a credential.

Do not run this on an untrusted or shared network (a co-working space, a hotel, a guest VLAN)
without adding auth first. If you need it, the natural v1.1 is a static bearer token in a header,
checked in a one-line middleware, with the token stored next to `state.json`.
