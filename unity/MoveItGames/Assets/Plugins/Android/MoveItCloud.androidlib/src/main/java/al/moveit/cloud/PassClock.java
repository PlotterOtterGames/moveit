package al.moveit.cloud;

/**
 * The time the box trusts for Game Pass, in unix seconds. Turning the box's clock back must not extend a pass,
 * and cheap boxes often have no clock battery (the date resets at power loss until the network fixes it).
 *
 * Order of trust:
 * 1. A signed server time (the "now" in every Game Pass token) plus the time elapsed since it arrived, while the
 *    box hasn't rebooted (elapsedRealtime is monotonic and can't be set by the user).
 * 2. Otherwise the larger of the system clock and the floor: the highest trusted time seen before, saved every
 *    minute while running. So an offline box's clock never goes backwards; at worst the pass "pauses" while an
 *    offline box is switched off, which is accepted (it needs the network to get a pass in the first place).
 * A signed server time also resets the floor, so a box whose clock was wrongly far ahead recovers when online.
 */
public final class PassClock {
    /** Where the monotonic and wall clocks come from (SystemClock and System on the box, fakes in tests). */
    public interface Source {
        long wallMillis();

        long elapsedMillis();

        /** Settings.Global.BOOT_COUNT, or -1 if unknown. */
        long bootCount();
    }

    static final String ANCHOR_SERVER = "clock.anchor_server";
    static final String ANCHOR_ELAPSED = "clock.anchor_elapsed";
    static final String ANCHOR_BOOT = "clock.anchor_boot";
    static final String FLOOR = "clock.floor";

    private final KeyValueStore store;
    private final Source source;

    public PassClock(KeyValueStore store, Source source) {
        this.store = store;
        this.source = source;
    }

    /** Trusted now, unix seconds. */
    public synchronized long now() {
        long anchor = store.getLong(ANCHOR_SERVER, 0);
        if (anchor > 0 && sameBoot()) {
            return anchor + (source.elapsedMillis() - store.getLong(ANCHOR_ELAPSED, 0)) / 1000;
        }
        return Math.max(source.wallMillis() / 1000, store.getLong(FLOOR, 0));
    }

    /** True while the anchor's elapsedRealtime is from this boot. */
    private boolean sameBoot() {
        long boot = source.bootCount();
        long anchorBoot = store.getLong(ANCHOR_BOOT, -2);
        if (boot >= 0 && anchorBoot >= 0 && boot != anchorBoot) return false;
        return source.elapsedMillis() >= store.getLong(ANCHOR_ELAPSED, Long.MAX_VALUE);
    }

    /** A verified server time arrived (from a Game Pass token). */
    public synchronized void onServerTime(long serverSeconds) {
        store.put(ANCHOR_SERVER, serverSeconds, ANCHOR_ELAPSED, source.elapsedMillis(), ANCHOR_BOOT, source.bootCount(), FLOOR, serverSeconds);
    }

    /** Saves the floor (call about once a minute while running and when pausing). It only moves forward. */
    public synchronized void persistFloor() {
        long t = now();
        if (t > store.getLong(FLOOR, 0)) store.put(FLOOR, t);
    }
}
