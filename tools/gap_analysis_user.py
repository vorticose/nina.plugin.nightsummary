import re
from datetime import datetime, timedelta

LOG_FILE = r"C:\Users\Evan\Downloads\20260408-224006-3.2.0.9001.13536-202604.log"
SESSION_START = datetime(2026, 4, 8, 22, 40, 24)
SESSION_END = datetime(2026, 4, 9, 6, 41, 29)

starts = {}
events = []
exposure_start = None
exposure_requested = 0

KNOWN_ITEMS = {
    'SwitchFilter', 'RunAutofocus', 'MoveFocuserByTemperature', 'MoveFocuserAbsolute',
    'MoveFocuserRelative', 'Dither', 'StartGuiding', 'StopGuiding',
    'SlewScopeToRaDec', 'SlewScopeToAltAz', 'ParkScope', 'UnparkScope', 'FindHome',
    'SetTracking', 'Center', 'CenterAndRotate', 'SolveAndSync', 'SolveAndRotate',
    'SynchronizeDome', 'OpenDomeShutter', 'CloseDomeShutter', 'ParkDome',
    'SetBrightness', 'ToggleLight', 'OpenCover', 'CloseCover',
    'CoolCamera', 'WarmCamera', 'MoveRotatorMechanical', 'SetSwitchValue',
    'WaitUntilSafe', 'MeridianFlip', 'TakeExposure', 'TakeSubframeExposure'
}

item_re = re.compile(r'Item:\s*(\w+)')
exp_time_re = re.compile(r'ExposureTime (\d+(?:\.\d+)?)')

with open(LOG_FILE, 'r') as f:
    for line in f:
        parts = line.strip().split('|')
        if len(parts) < 6: continue
        try: ts = datetime.fromisoformat(parts[0])
        except: continue
        if ts < SESSION_START or ts > SESSION_END: continue
        if parts[1] != 'INFO': continue
        source, member = parts[2], parts[3]
        msg = '|'.join(parts[5:])

        if source == 'ImageSaveController.cs' and member == 'DoWork':
            dur_match = re.search(r'Duration Total:\s*(\d+):(\d+):([\d.]+)', msg)
            if dur_match:
                h, m, s = int(dur_match.group(1)), int(dur_match.group(2)), float(dur_match.group(3))
                dur = h*3600 + m*60 + s
                events.append((ts - timedelta(seconds=dur), ts, 'ImageSave', dur))
            continue
        if source == 'ImageSolver.cs' and member == 'Solve':
            if 'Platesolving with parameters' in msg:
                starts['_platesolve'] = ts
            elif ('Platesolve successful' in msg or 'Platesolve failed' in msg) and '_platesolve' in starts:
                dur = (ts - starts['_platesolve']).total_seconds()
                events.append((starts['_platesolve'], ts, 'PlateSolve', dur))
                del starts['_platesolve']
            continue
        # Meridian flip from AscomTelescope.cs
        if source == 'AscomTelescope.cs' and member == 'MeridianFlip':
            if msg.startswith('Slewing to coordinates'):
                starts['_mflip'] = ts
            elif msg.startswith('Finished slewing') and '_mflip' in starts:
                dur = (ts - starts['_mflip']).total_seconds()
                events.append((starts['_mflip'], ts, 'MeridianFlip', dur))
                del starts['_mflip']
            continue
        if source != 'SequenceItem.cs' or member != 'Run': continue
        m_item = item_re.search(msg)
        if not m_item: continue
        item = m_item.group(1)

        if item in ('TakeExposure', 'TakeSubframeExposure'):
            if msg.startswith('Starting '):
                exposure_start = ts
                exp_match = exp_time_re.search(msg)
                exposure_requested = float(exp_match.group(1)) if exp_match else 0
            elif msg.startswith('Finishing ') and exposure_start:
                dur = (ts - exposure_start).total_seconds()
                events.append((exposure_start, ts, 'Exposure', dur))
                if exposure_requested > 0 and dur > exposure_requested:
                    dl = dur - exposure_requested
                    events.append((exposure_start + timedelta(seconds=exposure_requested), ts, 'CameraDownload', dl))
                exposure_start = None
        elif item in KNOWN_ITEMS:
            if msg.startswith('Starting '): starts[item] = ts
            elif msg.startswith('Finishing ') and item in starts:
                dur = (ts - starts[item]).total_seconds()
                events.append((starts[item], ts, item, dur))
                del starts[item]

events.sort(key=lambda e: e[0])

# Compute window
window_start = min(e[0] for e in events)
window_end = max(e[1] for e in events)
window_sec = (window_end - window_start).total_seconds()
total_integration = sum(e[3] for e in events if e[2] == 'Exposure')
total_overhead_events = sum(e[3] for e in events if e[2] not in ('Exposure', 'CameraDownload'))

print(f"Session: {SESSION_START} to {SESSION_END}")
print(f"Overhead window: {window_start.strftime('%H:%M:%S')} to {window_end.strftime('%H:%M:%S')}")
print(f"Window duration: {window_sec:.0f}s = {window_sec/3600:.2f}h")
print(f"Total integration: {total_integration:.0f}s = {total_integration/3600:.2f}h")
print(f"Implied overhead: {window_sec - total_integration:.0f}s = {(window_sec - total_integration)/60:.1f}m")
print(f"Exposures: {len([e for e in events if e[2] == 'Exposure'])}")

# Merge intervals
intervals = [(e[0], e[1]) for e in events]
intervals.sort()
merged = []
for start, end in intervals:
    if merged and start <= merged[-1][1]:
        merged[-1] = (merged[-1][0], max(merged[-1][1], end))
    else:
        merged.append((start, end))

# Find gaps
gaps = []
prev_end = window_start
for start, end in merged:
    if start > prev_end:
        gap_sec = (start - prev_end).total_seconds()
        gaps.append((prev_end, start, gap_sec))
    prev_end = max(prev_end, end)

total_gap = sum(g[2] for g in gaps)
print(f"\nTotal gap time: {total_gap:.0f}s = {total_gap/60:.1f}m")

# Breakdown by size
gaps_by_size = sorted(gaps, key=lambda g: -g[2])
print(f"\n{'='*70}")
print(f"LARGEST GAPS (top 20):")
print(f"{'='*70}")
for prev, nxt, sec in gaps_by_size[:20]:
    before = [e for e in events if abs((e[1] - prev).total_seconds()) < 1]
    after = [e for e in events if abs((e[0] - nxt).total_seconds()) < 1]
    b = before[-1][2] if before else "?"
    a = after[0][2] if after else "?"
    print(f"  {sec:6.1f}s  {prev.strftime('%H:%M:%S')} -> {nxt.strftime('%H:%M:%S')}  [{b}] -> [{a}]")

# Distribution
small = [g for g in gaps if g[2] <= 3]
medium = [g for g in gaps if 3 < g[2] <= 10]
large = [g for g in gaps if 10 < g[2] <= 60]
huge = [g for g in gaps if g[2] > 60]

print(f"\n{'='*70}")
print(f"GAP DISTRIBUTION:")
print(f"{'='*70}")
print(f"  <=3s:   {sum(g[2] for g in small):6.0f}s = {sum(g[2] for g in small)/60:5.1f}m  ({len(small)} gaps, avg {sum(g[2] for g in small)/max(len(small),1):.1f}s)")
print(f"  3-10s:  {sum(g[2] for g in medium):6.0f}s = {sum(g[2] for g in medium)/60:5.1f}m  ({len(medium)} gaps, avg {sum(g[2] for g in medium)/max(len(medium),1):.1f}s)")
print(f"  10-60s: {sum(g[2] for g in large):6.0f}s = {sum(g[2] for g in large)/60:5.1f}m  ({len(large)} gaps, avg {sum(g[2] for g in large)/max(len(large),1):.1f}s)")
print(f"  >60s:   {sum(g[2] for g in huge):6.0f}s = {sum(g[2] for g in huge)/60:5.1f}m  ({len(huge)} gaps)")

# What's happening in the log during the biggest gaps?
print(f"\n{'='*70}")
print(f"INVESTIGATING TOP 5 GAPS:")
print(f"{'='*70}")
