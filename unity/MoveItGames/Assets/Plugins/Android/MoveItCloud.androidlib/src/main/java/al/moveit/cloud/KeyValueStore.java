package al.moveit.cloud;

/** Small persistent settings (SharedPreferences on the box, a map in tests). Writes must be durable when they return. */
public interface KeyValueStore {
    String getString(String key, String fallback);

    long getLong(String key, long fallback);

    /** Applies all changes together and durably (one commit). */
    void put(Object... keysAndValues);

    void remove(String... keys);
}
