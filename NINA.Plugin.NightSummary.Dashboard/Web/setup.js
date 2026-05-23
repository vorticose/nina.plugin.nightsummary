// Setup wizard — vanilla JS state machine. 5 steps; back navigation OK in
// 1→4, no back after the first sync starts. State is per-tab (closing the
// tab restarts at step 1) and the companion doesn't persist mid-wizard
// progress — fresh users always land on welcome.

(function () {
    'use strict';

    const state = {
        step: 1,
        host: '',
        port: 8181,
        nsVersion: null,
        ninaVersion: null,
        probeOk: false,
        token: '',
        companionName: '',
        companionId: null,
        pollHours: 4,
        onBoot: true,
        acceptPush: true,
        dashboardPort: 8182,
    };

    // ── DOM helpers ──────────────────────────────────────────────────────

    function $(id)  { return document.getElementById(id); }
    function $$(s)  { return Array.from(document.querySelectorAll(s)); }

    function showStep(n) {
        state.step = n;
        $$('.step').forEach(s => {
            s.hidden = parseInt(s.dataset.step, 10) !== n;
        });
        $$('.steps li').forEach(li => {
            const i = parseInt(li.dataset.step, 10);
            li.classList.toggle('active', i === n);
            li.classList.toggle('done', i < n);
        });
        window.scrollTo(0, 0);
    }

    function setStatus(elId, text, kind /* 'error' | 'success' | null */) {
        const el = $(elId);
        if (!el) return;
        if (!text) { el.hidden = true; el.textContent = ''; el.className = 'status'; return; }
        el.hidden = false;
        el.textContent = text;
        el.className = 'status' + (kind ? ' ' + kind : '');
    }

    // ── API calls ────────────────────────────────────────────────────────

    async function probePrimary(host, port) {
        const url = `/api/setup/probe?host=${encodeURIComponent(host)}&port=${encodeURIComponent(port)}`;
        const resp = await fetch(url);
        return resp.json();
    }

    async function claimPair(host, port, token, companionName) {
        const resp = await fetch('/api/setup/claim', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ host, port, token, companionName }),
        });
        return resp.json();
    }

    async function saveConfig() {
        const resp = await fetch('/api/companion/config', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                host: state.host,
                port: state.port,
                onBoot: state.onBoot,
                // Single user-facing knob; failure interval is derived
                // server-side / hardcoded to a sensible default.
                pollingIntervalHoursOnSuccess:   state.pollHours,
                pollingIntervalMinutesOnFailure: 30,
                acceptPush: state.acceptPush,
                // dashboardPort is the companion's own listener port. Save
                // takes effect on next companion restart (server is already
                // bound to the old port for the current process). The hint
                // text in step 4 tells the user.
                dashboardPort: state.dashboardPort,
            }),
        });
        return resp.json();
    }

    async function triggerSync() {
        const resp = await fetch('/api/companion/sync', { method: 'POST' });
        return resp.json();
    }

    async function pollStatus() {
        const resp = await fetch('/api/companion/status');
        return resp.json();
    }

    // ── Step handlers ────────────────────────────────────────────────────

    function bind() {
        // Step 1
        $$('.step[data-step="1"] [data-action="next"]').forEach(b =>
            b.addEventListener('click', () => showStep(2)));

        // Step 2
        $('testBtn').addEventListener('click', onProbeClick);
        $$('.step[data-step="2"] [data-action="back"]').forEach(b =>
            b.addEventListener('click', () => showStep(1)));
        $('nextBtn2').addEventListener('click', () => {
            captureStep2();
            showStep(3);
        });

        // Step 3
        $('pairBtn').addEventListener('click', onPairClick);
        $$('.step[data-step="3"] [data-action="back"]').forEach(b =>
            b.addEventListener('click', () => showStep(2)));

        // Step 4
        $('saveBtn').addEventListener('click', onSaveClick);
        $$('.step[data-step="4"] [data-action="back"]').forEach(b =>
            b.addEventListener('click', () => showStep(3)));

        // Step 5
        $('retryBtn').addEventListener('click', runFirstSync);
        $('goDashboardBtn').addEventListener('click', () => { window.location.href = '/'; });
    }

    function captureStep2() {
        state.host = ($('host').value || '').trim();
        state.port = parseInt($('port').value, 10) || 8181;
    }

    async function onProbeClick() {
        captureStep2();
        if (!state.host) {
            setStatus('probeStatus', 'Enter a host or IP first.', 'error');
            return;
        }
        setStatus('probeStatus', 'Testing connection…', null);
        $('testBtn').disabled = true;
        try {
            const r = await probePrimary(state.host, state.port);
            if (!r.ok) {
                setStatus('probeStatus', r.error || 'Could not reach NINA.', 'error');
                $('nextBtn2').disabled = true;
                return;
            }
            if (!r.hasNs) {
                setStatus('probeStatus', 'Reached the server but Night Summary is not installed. Install the plugin in NINA first.', 'error');
                $('nextBtn2').disabled = true;
                return;
            }
            state.nsVersion = r.nsVersion || null;
            state.ninaVersion = r.ninaVersion || null;
            state.probeOk = true;
            setStatus('probeStatus',
                `✓ Connected — Night Summary v${r.nsVersion || '?'} on NINA ${r.ninaVersion || '?'}` +
                (r.pairedCount > 0 ? ` (${r.pairedCount} companion${r.pairedCount === 1 ? '' : 's'} already paired)` : ''),
                'success');
            $('nextBtn2').disabled = false;
        } finally {
            $('testBtn').disabled = false;
        }
    }

    async function onPairClick() {
        state.token = ($('token').value || '').trim();
        state.companionName = ($('companionName').value || '').trim();
        if (!state.token) {
            setStatus('pairStatus', 'Paste the token from NINA first.', 'error');
            return;
        }
        if (!state.companionName) {
            setStatus('pairStatus', 'Give this companion a name (any string).', 'error');
            return;
        }
        setStatus('pairStatus', 'Pairing…', null);
        $('pairBtn').disabled = true;
        try {
            const r = await claimPair(state.host, state.port, state.token, state.companionName);
            if (r.ok) {
                state.companionId = r.companionId || null;
                setStatus('pairStatus', '✓ Paired successfully.', 'success');
                setTimeout(() => showStep(4), 400);
                return;
            }
            const friendly = pairErrorMessage(r);
            setStatus('pairStatus', friendly, 'error');
        } finally {
            $('pairBtn').disabled = false;
        }
    }

    function pairErrorMessage(r) {
        switch (r.errorCode) {
            case 'unknown_token':
                return 'That token is not recognized. Generate a fresh one in NINA and try again.';
            case 'revoked':
                return 'That token has been revoked. Generate a fresh one in NINA.';
            case 'already_paired':
                return `That token is already paired with "${r.alreadyPairedCompanionName || 'another companion'}". Revoke it in NINA first, or generate a new token.`;
            case 'timeout':
                return 'Connection to NINA timed out. Check the host is still reachable.';
            default:
                return r.error || 'Pairing failed for an unknown reason.';
        }
    }

    async function onSaveClick() {
        state.pollHours     = parseInt($('pollHours').value, 10) || 4;
        state.onBoot        = !!$('onBoot').checked;
        state.acceptPush    = !!$('acceptPush').checked;
        state.dashboardPort = parseInt($('dashboardPort').value, 10) || 8182;

        setStatus('saveStatus', 'Saving…', null);
        $('saveBtn').disabled = true;
        try {
            const r = await saveConfig();
            if (!r.ok) {
                setStatus('saveStatus', r.error || 'Save failed.', 'error');
                return;
            }
            showStep(5);
            runFirstSync();
        } finally {
            $('saveBtn').disabled = false;
        }
    }

    async function runFirstSync() {
        $('retryBtn').hidden = true;
        $('goDashboardBtn').hidden = true;
        $('syncProgress').textContent = 'Starting sync…';
        setStatus('syncStatus', null);

        try {
            const r = await triggerSync();
            // The trigger response IS the final state — coalesced inside the
            // controller, so we don't need to poll separately. Surface any
            // sync error verbatim; the wizard exposes Retry on failure.
            if (r.lastError) {
                $('syncProgress').textContent = 'Sync failed.';
                setStatus('syncStatus', r.lastError, 'error');
                $('retryBtn').hidden = false;
                $('goDashboardBtn').hidden = false; // user can still proceed; manual sync available
                return;
            }
            $('syncProgress').textContent =
                `✓ Setup complete — pulled ${r.filesAdded || 0} files, ${r.thumbsAdded || 0} thumbnails.`;
            setStatus('syncStatus', 'Redirecting to the dashboard…', 'success');
            setTimeout(() => { window.location.href = '/'; }, 1500);
        } catch (e) {
            $('syncProgress').textContent = 'Sync failed.';
            setStatus('syncStatus', e.message || String(e), 'error');
            $('retryBtn').hidden = false;
            $('goDashboardBtn').hidden = false;
        }
    }

    // ── Boot ─────────────────────────────────────────────────────────────

    document.addEventListener('DOMContentLoaded', () => {
        bind();
        showStep(1);
    });
})();
