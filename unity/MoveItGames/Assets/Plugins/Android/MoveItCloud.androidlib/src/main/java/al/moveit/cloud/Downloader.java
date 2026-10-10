package al.moveit.cloud;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.RandomAccessFile;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;

/**
 * Downloads an update file into app-private storage and only hands it over when its size and SHA-256 match the
 * signed manifest. Resumes after a dropped connection or a power cut (HTTP Range on the ".part" file); a file that
 * turns out wrong is deleted, never installed.
 */
public final class Downloader {
    public interface Progress {
        void onProgress(long done, long total);
    }

    public static final class Failed extends IOException {
        private static final long serialVersionUID = 1L;

        public Failed(String message) {
            super(message);
        }
    }

    private Downloader() {}

    /**
     * Returns dir/<sha256>.<extension>, verified. Reuses an already verified file.
     * @param minFreeBytes refuse to start if the disk would have less than this left afterwards
     */
    public static File fetch(String url, File dir, String sha256, long size, String extension, long minFreeBytes, Progress progress)
            throws IOException {
        if (!url.startsWith("https://") && !url.startsWith("http://127.0.0.1") && !url.startsWith("http://localhost")) {
            throw new Failed("only https downloads");
        }
        if (!dir.isDirectory() && !dir.mkdirs()) throw new Failed("cannot create " + dir);
        File done = new File(dir, sha256 + "." + extension);
        if (done.isFile() && done.length() == size && sha256.equals(sha256Of(done))) return done;
        if (done.exists() && !done.delete()) throw new Failed("cannot delete " + done);
        File part = new File(dir, sha256 + ".part");
        long have = part.isFile() ? part.length() : 0;
        if (have > size) {
            part.delete();
            have = 0;
        }
        if (dir.getUsableSpace() < (size - have) + minFreeBytes) throw new Failed("not enough free space");

        if (have < size) {
            HttpURLConnection http = (HttpURLConnection) new URL(url).openConnection();
            http.setConnectTimeout(15_000);
            http.setReadTimeout(30_000);
            http.setInstanceFollowRedirects(false);
            if (have > 0) http.setRequestProperty("Range", "bytes=" + have + "-");
            try {
                int code = http.getResponseCode();
                if (code == 200) {
                    have = 0; // server ignored the range: start over
                } else if (code != 206) {
                    throw new Failed("HTTP " + code);
                }
                long expected = http.getContentLengthLong();
                if (expected >= 0 && have + expected != size) throw new Failed("server size differs from the manifest");
                try (InputStream in = http.getInputStream(); RandomAccessFile out = new RandomAccessFile(part, "rw")) {
                    out.setLength(have);
                    out.seek(have);
                    byte[] buffer = new byte[64 * 1024];
                    int n;
                    while ((n = in.read(buffer)) > 0) {
                        if (have + n > size) throw new Failed("more data than the manifest says");
                        out.write(buffer, 0, n);
                        have += n;
                        if (progress != null) progress.onProgress(have, size);
                    }
                    out.getFD().sync();
                }
            } finally {
                http.disconnect();
            }
        }
        if (part.length() != size) throw new Failed("incomplete (" + part.length() + " of " + size + ")");
        String actual = sha256Of(part);
        if (!sha256.equals(actual)) {
            part.delete();
            throw new Failed("hash mismatch: the file was damaged or replaced");
        }
        if (!part.renameTo(done)) throw new Failed("cannot rename " + part);
        return done;
    }

    public static String sha256Of(File file) throws IOException {
        try (InputStream in = new FileInputStream(file)) {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] buffer = new byte[64 * 1024];
            int n;
            while ((n = in.read(buffer)) > 0) digest.update(buffer, 0, n);
            return hex(digest.digest());
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    static String hex(byte[] bytes) {
        StringBuilder sb = new StringBuilder(bytes.length * 2);
        for (byte b : bytes) sb.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
        return sb.toString();
    }
}
