package al.moveit.cloud;

import static org.junit.Assert.assertEquals;

import org.junit.Test;

/** Turning the clock back, pulling the plug and boxes without a clock battery. */
public class PassClockTest {
    private final TestSupport.MemoryStore store = new TestSupport.MemoryStore();
    private final TestSupport.FakeTime time = new TestSupport.FakeTime();
    private final PassClock clock = new PassClock(store, time);

    @Test
    public void withoutServerTimeItUsesTheSystemClock() {
        time.wall = 1_760_000_000_000L;
        assertEquals(1_760_000_000L, clock.now());
    }

    @Test
    public void afterServerTimeTheSystemClockNoLongerMatters() {
        time.wall = 1_760_000_000_000L;
        time.elapsed = 10_000;
        clock.onServerTime(1_760_000_000L);
        time.advance(3600);
        time.wall = 1_000_000_000_000L; // set back by years
        assertEquals(1_760_003_600L, clock.now());
        time.wall = 1_900_000_000_000L; // or forward
        assertEquals(1_760_003_600L, clock.now());
    }

    @Test
    public void afterARebootTheClockNeverGoesBehindTheLastTrustedTime() {
        time.wall = 1_760_000_000_000L;
        clock.onServerTime(1_760_000_000L);
        time.advance(600);
        clock.persistFloor();
        time.reboot();
        time.wall = 0; // no clock battery: 1970 until the network fixes it
        assertEquals(1_760_000_600L, clock.now());
        time.wall = 1_770_000_000_000L; // the network set the right time
        assertEquals(1_770_000_000L, clock.now());
    }

    @Test
    public void aClockThatWasWronglyFarAheadIsCorrectedByTheServer() {
        time.wall = 2_000_000_000_000L; // wrong, far ahead
        clock.persistFloor();
        assertEquals(2_000_000_000L, clock.now());
        clock.onServerTime(1_760_000_000L);
        time.reboot();
        time.wall = 0;
        assertEquals(1_760_000_000L, clock.now());
    }

    @Test
    public void withoutBootCountARebootIsSeenFromTheMonotonicClock() {
        time.boot = -1;
        time.wall = 1_760_000_000_000L;
        time.elapsed = 1_000_000;
        clock.onServerTime(1_760_000_000L);
        time.elapsed = 2_000; // monotonic clock restarted: rebooted
        time.wall = 0;
        assertEquals(1_760_000_000L, clock.now());
    }
}
