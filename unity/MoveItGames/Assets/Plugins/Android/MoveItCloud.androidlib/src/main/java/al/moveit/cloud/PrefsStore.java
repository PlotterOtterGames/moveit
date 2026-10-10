package al.moveit.cloud;

import android.content.Context;
import android.content.SharedPreferences;

/** KeyValueStore on SharedPreferences. commit() (not apply()): a write is on disk when put() returns. */
final class PrefsStore implements KeyValueStore {
    private final SharedPreferences prefs;

    PrefsStore(Context context, String name) {
        prefs = context.getSharedPreferences(name, Context.MODE_PRIVATE);
    }

    @Override
    public String getString(String key, String fallback) {
        try {
            return prefs.getString(key, fallback);
        } catch (ClassCastException e) {
            return fallback;
        }
    }

    @Override
    public long getLong(String key, long fallback) {
        try {
            return prefs.getLong(key, fallback);
        } catch (ClassCastException e) {
            return fallback;
        }
    }

    @Override
    public void put(Object... kv) {
        SharedPreferences.Editor e = prefs.edit();
        for (int i = 0; i < kv.length; i += 2) {
            String key = (String) kv[i];
            Object v = kv[i + 1];
            if (v == null) e.remove(key);
            else if (v instanceof String) e.putString(key, (String) v);
            else if (v instanceof Number) e.putLong(key, ((Number) v).longValue());
            else throw new IllegalArgumentException("unsupported value for " + key);
        }
        e.commit();
    }

    @Override
    public void remove(String... keys) {
        SharedPreferences.Editor e = prefs.edit();
        for (String k : keys) e.remove(k);
        e.commit();
    }
}
