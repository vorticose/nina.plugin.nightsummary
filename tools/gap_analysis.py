import re
from datetime import datetime, timedelta

LOG_FILE = r"K:\Remote Astro\Logs\20260408-215559-3.2.0.9001.2608-202604.log"
SESSION_START = datetime(2026, 4, 8, 22, 28, 45)
SESSION_END = datetime(2026, 4, 9, 6, 37, 49)

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
            elif 'Platesolve successful' in msg and '_platesolve' in starts:
                dur = (ts - starts['_platesolve']).total_seconds()
                events.append((starts['_platesolve'], ts, 'PlateSolve', dur))
                del starts['_platesolve']
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

# Overhead window: min/max of all events (excluding aborted)
window_start = min(e[0] for e in events)
window_end = max(e[1] for e in events)
print(f"Overhead window: {window_start.strftime('%H:%M:%S')} -> {window_end.strftime('%H:%M:%S')}")
print(f"Window duration: {(window_end-window_start).total_seconds():.0f}s = {(window_end-window_start).total_seconds()/3600:.2f}h")

# Roof-closed at 05:00:16, aborted exposure extends back to 04:57:13
ROOF_START = datetime(2026, 4, 9, 4, 57, 13)  # extended back
ROOF_END = window_end  # orphaned, extends to window end (05:02:50)
print(f"Roof closed (extended): {ROOF_START.strftime('%H:%M:%S')} -> {ROOF_END.strftime('%H:%M:%S')}")
print(f"Roof closed duration: {(ROOF_END-ROOF_START).total_seconds():.0f}s = {(ROOF_END-ROOF_START).total_seconds()/60:.1f}m")

effective_window = (window_end - window_start).total_seconds() - (ROOF_END - ROOF_START).total_seconds()
total_integration = sum(e[3] for e in events if e[2] == 'Exposure')
implied_overhead = effective_window - total_integration
print(f"\nEffective window: {effective_window:.0f}s = {effective_window/3600:.2f}h")
print(f"Total integration: {total_integration:.0f}s = {total_integration/3600:.2f}h")
print(f"Implied overhead: {implied_overhead:.0f}s = {implied_overhead/60:.1f}m")

# Now find all gaps outside roof-closed zone
# Merge all event intervals
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
        # Check if gap is within roof-closed zone
        in_roof = prev_end >= ROOF_START and start <= ROOF_END
        gaps.append((prev_end, start, gap_sec, in_roof))
    prev_end = max(prev_end, end)

# Non-roof gaps
non_roof_gaps = [(g[0], g[1], g[2]) for g in gaps if not g[3]]
non_roof_gaps.sort(key=lambda g: -g[2])

print(f"\n{'='*70}")
print(f"NON-ROOF-CLOSED GAPS > 1 second:")
print(f"{'='*70}")
total_gap = 0
for prev, nxt, sec in non_roof_gaps:
    if sec < 1: continue
    total_gap += sec
    before = [e for e in events if abs((e[1] - prev).total_seconds()) < 1]
    after = [e for e in events if abs((e[0] - nxt).total_seconds()) < 1]
    b = before[-1][2] if before else "SESSION_START"
    a = after[0][2] if after else "SESSION_END"
    print(f"  {sec:6.1f}s  {prev.strftime('%H:%M:%S')} -> {nxt.strftime('%H:%M:%S')}  [{b}] -> [{a}]")

print(f"\nTotal non-roof gaps: {total_gap:.0f}s = {total_gap/60:.1f}m")

# Breakdown
small = sum(g[2] for g in non_roof_gaps if g[2] <= 3)
medium = sum(g[2] for g in non_roof_gaps if 3 < g[2] <= 10)
large = sum(g[2] for g in non_roof_gaps if g[2] > 10)
print(f"  <=3s (inter-item):  {small:.0f}s = {small/60:.1f}m  ({len([g for g in non_roof_gaps if g[2] <= 3])} gaps)")
print(f"  3-10s (triggers):   {medium:.0f}s = {medium/60:.1f}m  ({len([g for g in non_roof_gaps if 3 < g[2] <= 10])} gaps)")
print(f"  >10s (structural):  {large:.0f}s = {large/60:.1f}m  ({len([g for g in non_roof_gaps if g[2] > 10])} gaps)")
