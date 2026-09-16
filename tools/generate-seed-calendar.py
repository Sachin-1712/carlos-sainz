#!/usr/bin/env python3
"""Generates data/seed/calendar-2026.json.

EVERY DATE AND TIME HERE IS UNVERIFIED. The table below was written from memory, not from the
official calendar, and the session times are a standard template, not published timetables.
The output carries "verified": false on every event for that reason. Replace it with a live
sync, or check each row against the official F1 calendar and set verified to true by hand.
"""
import datetime as dt
import json
from pathlib import Path
from zoneinfo import ZoneInfo

# round, official name, circuit, country, IANA zone, sprint weekend?, race date, race local time
EVENTS = [
    (1,  "Australian Grand Prix",     "Albert Park Circuit",               "Australia",            "Australia/Melbourne", False, "2026-03-08", "15:00"),
    (2,  "Chinese Grand Prix",        "Shanghai International Circuit",    "China",                "Asia/Shanghai",       True,  "2026-03-15", "15:00"),
    (3,  "Japanese Grand Prix",       "Suzuka International Racing Course","Japan",                "Asia/Tokyo",          False, "2026-03-29", "14:00"),
    (4,  "Bahrain Grand Prix",        "Bahrain International Circuit",     "Bahrain",              "Asia/Bahrain",        False, "2026-04-12", "18:00"),
    (5,  "Saudi Arabian Grand Prix",  "Jeddah Corniche Circuit",           "Saudi Arabia",         "Asia/Riyadh",         False, "2026-04-19", "20:00"),
    (6,  "Miami Grand Prix",          "Miami International Autodrome",     "United States",        "America/New_York",    True,  "2026-05-03", "16:00"),
    (7,  "Canadian Grand Prix",       "Circuit Gilles Villeneuve",         "Canada",               "America/Toronto",     True,  "2026-05-24", "14:00"),
    (8,  "Monaco Grand Prix",         "Circuit de Monaco",                 "Monaco",               "Europe/Monaco",       False, "2026-06-07", "15:00"),
    (9,  "Spanish Grand Prix",        "Circuit de Barcelona-Catalunya",    "Spain",                "Europe/Madrid",       False, "2026-06-14", "15:00"),
    (10, "Austrian Grand Prix",       "Red Bull Ring",                     "Austria",              "Europe/Vienna",       False, "2026-06-28", "15:00"),
    (11, "British Grand Prix",        "Silverstone Circuit",               "United Kingdom",       "Europe/London",       True,  "2026-07-05", "15:00"),
    (12, "Belgian Grand Prix",        "Circuit de Spa-Francorchamps",      "Belgium",              "Europe/Brussels",     False, "2026-07-19", "15:00"),
    (13, "Hungarian Grand Prix",      "Hungaroring",                       "Hungary",              "Europe/Budapest",     False, "2026-07-26", "15:00"),
    (14, "Dutch Grand Prix",          "Circuit Zandvoort",                 "Netherlands",          "Europe/Amsterdam",    True,  "2026-08-23", "15:00"),
    (15, "Italian Grand Prix",        "Autodromo Nazionale Monza",         "Italy",                "Europe/Rome",         False, "2026-09-06", "15:00"),
    (16, "Madrid Grand Prix",         "Madring",                           "Spain",                "Europe/Madrid",       False, "2026-09-13", "15:00"),
    (17, "Azerbaijan Grand Prix",     "Baku City Circuit",                 "Azerbaijan",           "Asia/Baku",           False, "2026-09-26", "15:00"),
    (18, "Singapore Grand Prix",      "Marina Bay Street Circuit",         "Singapore",            "Asia/Singapore",      True,  "2026-10-11", "20:00"),
    (19, "United States Grand Prix",  "Circuit of the Americas",           "United States",        "America/Chicago",     False, "2026-10-25", "14:00"),
    (20, "Mexico City Grand Prix",    "Autodromo Hermanos Rodriguez",      "Mexico",               "America/Mexico_City", False, "2026-11-01", "14:00"),
    (21, "Sao Paulo Grand Prix",      "Autodromo Jose Carlos Pace",        "Brazil",               "America/Sao_Paulo",   False, "2026-11-08", "14:00"),
    (22, "Las Vegas Grand Prix",      "Las Vegas Strip Circuit",           "United States",        "America/Los_Angeles", False, "2026-11-21", "20:00"),
    (23, "Qatar Grand Prix",          "Lusail International Circuit",      "Qatar",                "Asia/Qatar",          False, "2026-11-29", "19:00"),
    (24, "Abu Dhabi Grand Prix",      "Yas Marina Circuit",                "United Arab Emirates", "Asia/Dubai",          False, "2026-12-06", "17:00"),
]

H = dt.timedelta(hours=1)
M = dt.timedelta(minutes=1)

# Session templates as (type, day offset from race day, offset from race local time, duration).
CONVENTIONAL = [
    ("Practice1",  -2, -150 * M, 60 * M),
    ("Practice2",  -2,   60 * M, 60 * M),
    ("Practice3",  -1, -150 * M, 60 * M),
    ("Qualifying", -1,   60 * M, 60 * M),
    ("Race",        0,    0 * M, 120 * M),
]
SPRINT = [
    ("Practice1",        -2, -150 * M, 60 * M),
    ("SprintQualifying", -2,   90 * M, 60 * M),
    ("Sprint",           -1, -180 * M, 60 * M),
    ("Qualifying",       -1,   60 * M, 60 * M),
    ("Race",              0,    0 * M, 120 * M),
]


def utc(local: dt.datetime) -> str:
    return local.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def build(event):
    rnd, name, circuit, country, zone, sprint, race_date, race_time = event
    tz = ZoneInfo(zone)
    race_day = dt.date.fromisoformat(race_date)
    race_local = dt.datetime.combine(race_day, dt.time.fromisoformat(race_time), tzinfo=tz)
    sessions = []
    for stype, day_offset, time_offset, duration in (SPRINT if sprint else CONVENTIONAL):
        start = dt.datetime.combine(race_day + dt.timedelta(days=day_offset), race_local.timetz()) + time_offset
        sessions.append({"type": stype, "start": utc(start), "end": utc(start + duration)})
    return {
        "round": rnd,
        "officialName": name,
        "circuit": circuit,
        "country": country,
        "localTimeZoneId": zone,
        "format": "Sprint" if sprint else "Conventional",
        "status": "Scheduled",
        "verified": False,
        "sessions": sessions,
    }


doc = {
    "season": 2026,
    "disclaimer": (
        "UNVERIFIED. Dates were written from memory and session times follow a standard template; "
        "none of it has been checked against the official F1 calendar. Every event is marked "
        "verified: false. Use a live sync, or verify each row by hand before relying on it."
    ),
    "generatedBy": "tools/generate-seed-calendar.py",
    "events": [build(e) for e in EVENTS],
}

out = Path(__file__).resolve().parent.parent / "data" / "seed" / "calendar-2026.json"
out.write_text(json.dumps(doc, indent=2) + "\n")
print(f"wrote {out} with {len(doc['events'])} events")
