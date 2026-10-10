package al.moveit.cloud;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

public class CloudStateTest {
    static final long NOW = 1_760_000_000L;
    final TestSupport.Key passKey = new TestSupport.Key("p1");
    final TestSupport.Key releaseKey = new TestSupport.Key("r1");
    final TestSupport.MemoryStore store = new TestSupport.MemoryStore();
    final TestSupport.FakeTime time = new TestSupport.FakeTime();
    CloudState state;
    FakeServer server;

    /** Behaves like the Worker for one box. */
    class FakeServer implements CloudState.Api {
        String device = "d_" + "A".repeat(20);
        long passUntil = 0;
        long serverNow = NOW;
        IOException next; // thrown by the next call
        int calls;
        final List<String> redeemRequests = new ArrayList<>();
        final List<JSONArray> eventBatches = new ArrayList<>();

        String token(String forDevice, long until, long now) {
            return passKey.sign(TestSupport.obj("type", "tundu.pass", "v", 1, "device", forDevice, "pass_until", until, "now", now, "blocked", false));
        }

        private void maybeFail() throws IOException {
            calls++;
            if (next != null) {
                IOException e = next;
                next = null;
                throw e;
            }
        }

        @Override
        public CloudApi.Enrollment enroll(String model, int sdk, int app) throws IOException {
            maybeFail();
            CloudApi.Enrollment e = new CloudApi.Enrollment();
            e.device = device;
            e.secret = "secret";
            e.passEnvelope = token(device, passUntil, serverNow);
            return e;
        }

        @Override
        public String status(String d, String secret, int app, int sdk) throws IOException {
            maybeFail();
            return token(device, passUntil, serverNow);
        }

        @Override
        public CloudApi.RedeemAnswer redeem(String d, String secret, String code, String request) throws IOException {
            redeemRequests.add(request);
            maybeFail();
            passUntil = Math.max(passUntil, serverNow) + 30 * 86400L;
            CloudApi.RedeemAnswer a = new CloudApi.RedeemAnswer();
            a.result = "ok";
            a.days = 30;
            a.passEnvelope = token(device, passUntil, serverNow);
            return a;
        }

        @Override
        public void events(String d, String secret, int app, JSONArray events) throws IOException {
            maybeFail();
            eventBatches.add(events);
        }
    }

    @Before
    public void setUp() throws Exception {
        time.wall = NOW * 1000;
        time.elapsed = 10_000;
        state = new CloudState(store, new PassClock(store, time), releaseKey.asMap(), passKey.asMap(), "stable", "al.tundu.games");
        server = new FakeServer();
        assertTrue(state.ensureEnrolled(server, "TX66", 30, 10000));
    }

    String validCode() {
        String body = "ABCDEFGHJKLMNPQ";
        return GiftCode.format(body + GiftCode.checkChar(body));
    }

    @Test
    public void enrolsOnceAndStoresTheIdentity() throws Exception {
        assertEquals(server.device, state.deviceId());
        int calls = server.calls;
        state.ensureEnrolled(server, "TX66", 30, 10000);
        assertEquals("no second enrolment", calls, server.calls);
        assertNotNull(state.storedPass());
        assertFalse(state.passActive());
    }

    @Test
    public void redeemingOpensGamePassAndUsesTheServerClock() {
        time.wall = 0; // box clock reset to 1970
        CloudState.Redeemed r = state.redeem(server, validCode().toLowerCase());
        assertEquals("ok", r.result);
        assertEquals(30, r.days);
        assertTrue(state.passActive());
        assertEquals(NOW + 30 * 86400L, state.passUntil());
    }

    @Test
    public void aTypoIsCaughtWithoutCallingTheServer() {
        String code = validCode();
        String typo = code.substring(0, 18) + (code.charAt(18) == 'Z' ? 'Y' : 'Z');
        int calls = server.calls;
        assertEquals("check_code", state.redeem(server, typo).result);
        assertEquals(calls, server.calls);
    }

    @Test
    public void retryingAfterALostAnswerReusesTheRequestId() {
        server.next = new IOException("Wi-Fi dropped");
        assertEquals("network", state.redeem(server, validCode()).result);
        assertEquals("ok", state.redeem(server, validCode()).result);
        assertEquals(2, server.redeemRequests.size());
        assertEquals(server.redeemRequests.get(0), server.redeemRequests.get(1));
        state.redeem(server, validCode());
        assertNotEquals("a new attempt after an answer gets a new id", server.redeemRequests.get(1), server.redeemRequests.get(2));
    }

    @Test
    public void reportsLocksAndBlocks() {
        server.next = new CloudApi.HttpError(429, "locked", 80000);
        CloudState.Redeemed r = state.redeem(server, validCode());
        assertEquals("locked", r.result);
        assertEquals(80000, r.retryAfter);
        server.next = new CloudApi.HttpError(403, "blocked", 0);
        assertEquals("blocked", state.redeem(server, validCode()).result);
    }

    @Test
    public void refusesTokensForAnotherBoxOlderTokensAndTokensSignedWithTheReleaseKey() throws Exception {
        server.passUntil = NOW + 100 * 86400L;
        state.checkIn(server, 10000, 30);
        long until = state.passUntil();
        assertFalse("other device", state.acceptPass(server.token("d_" + "B".repeat(20), NOW + 999 * 86400L, NOW + 10), true));
        assertFalse("older token", state.acceptPass(server.token(server.device, NOW + 999 * 86400L, NOW - 10), true));
        String wrongRole = releaseKey.sign(TestSupport.obj("type", "tundu.pass", "v", 1, "device", server.device, "pass_until", NOW + 999 * 86400L, "now", NOW + 10, "blocked", false));
        assertFalse("release key", state.acceptPass(wrongRole, true));
        assertEquals(until, state.passUntil());
    }

    @Test
    public void turningTheClockBackDoesNotExtendThePass() throws Exception {
        server.passUntil = NOW + 86400;
        state.checkIn(server, 10000, 30);
        time.advance(2 * 86400);
        assertFalse(state.passActive());
        time.wall = NOW * 1000; // clock set back two days
        assertFalse(state.passActive());
        state.clock().persistFloor();
        time.reboot();
        time.wall = NOW * 1000;
        assertFalse("not after a reboot either", state.passActive());
    }

    @Test
    public void reEnrolsOnlyAfterRepeatedRefusalsAndKeepsPaidTime() throws Exception {
        server.passUntil = NOW + 60 * 86400L;
        state.checkIn(server, 10000, 30);
        String oldDevice = state.deviceId();
        for (int i = 0; i < 2; i++) {
            server.next = new CloudApi.HttpError(401, "unauthorized", 0);
            try {
                state.checkIn(server, 10000, 30);
            } catch (CloudApi.HttpError expected) {
                // still the same identity
            }
            assertEquals(oldDevice, state.deviceId());
        }
        server.next = new CloudApi.HttpError(401, "unauthorized", 0);
        try {
            state.checkIn(server, 10000, 30);
        } catch (CloudApi.HttpError expected) {
            // third refusal: identity dropped
        }
        assertNull(state.deviceId());
        assertEquals("old token still honoured", NOW + 60 * 86400L, state.passUntil());
        server.device = "d_" + "C".repeat(20);
        server.passUntil = 0;
        state.ensureEnrolled(server, "TX66", 30, 10000);
        assertEquals("d_" + "C".repeat(20), state.deviceId());
        assertTrue("paid time survives re-enrolment", state.passActive());
    }

    @Test
    public void manifestsNeverGoBackwards() {
        JSONObject m = ChannelManifestTest.manifest();
        assertTrue(state.acceptManifest(releaseKey.sign(m)));
        ChannelManifestTest.put(m, "serial", 4);
        assertFalse("rollback", state.acceptManifest(releaseKey.sign(m)));
        ChannelManifestTest.put(m, "serial", 5);
        assertTrue("same serial again is fine", state.acceptManifest(releaseKey.sign(m)));
        assertFalse("signed with the pass key", state.acceptManifest(passKey.sign(m)));
        assertEquals(5, state.manifest().serial);
    }

    @Test
    public void offersTheUpdateUntilItFailedThreeTimes() {
        state.acceptManifest(releaseKey.sign(ChannelManifestTest.manifest()));
        ChannelManifest.App app = state.pendingAppUpdate(10000, 30);
        assertNotNull(app);
        for (int i = 0; i < 3; i++) state.recordInstallFailure(app.sha256, "conflict");
        assertNull(state.pendingAppUpdate(10000, 30));
    }

    @Test
    public void healthEventsAreQueuedCappedAndSentInBatches() throws Exception {
        for (int i = 0; i < 60; i++) state.queueEvent("crash", "x" + i);
        server.next = new IOException("offline");
        try {
            state.flushEvents(server, 10000);
        } catch (IOException expected) {
            // kept for later
        }
        state.flushEvents(server, 10000);
        assertEquals(3, server.eventBatches.size());
        assertEquals(20, server.eventBatches.get(0).length());
        assertEquals("oldest dropped beyond 50", "x10", server.eventBatches.get(0).getJSONObject(0).getString("detail"));
        state.markHealthy(10000);
        state.markHealthy(10000);
        state.flushEvents(server, 10000);
        assertEquals("boot_ok once per version", 1, server.eventBatches.get(3).length());
    }

    @Test
    public void countsStartsThatNeverReachedTheHomeScreen() {
        assertEquals(1, state.onLaunch());
        assertEquals(2, state.onLaunch());
        state.markHealthy(10000);
        assertEquals(1, state.onLaunch());
    }
}
