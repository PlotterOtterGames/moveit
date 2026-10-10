package al.moveit.cloud;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.fail;

import com.sun.net.httpserver.HttpServer;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;

import java.io.File;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.file.Files;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/** Resume after a dropped connection, and never hand over a file whose hash or size is wrong. */
public class DownloaderTest {
    HttpServer server;
    byte[] content = new byte[300_000];
    String sha;
    volatile int cutAfter = -1; // drop the connection after this many bytes (once)
    volatile boolean ignoreRange;
    final List<String> ranges = new ArrayList<>();
    File dir;

    @Before
    public void setUp() throws Exception {
        new Random(7).nextBytes(content);
        sha = Downloader.hex(MessageDigest.getInstance("SHA-256").digest(content));
        dir = Files.createTempDirectory("tundu-dl").toFile();
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/f", exchange -> {
            String range = exchange.getRequestHeaders().getFirst("Range");
            synchronized (ranges) {
                ranges.add(range);
            }
            int from = 0;
            if (range != null && !ignoreRange) from = Integer.parseInt(range.replaceAll("bytes=(\\d+)-", "$1"));
            int length = content.length - from;
            exchange.sendResponseHeaders(from > 0 ? 206 : 200, length);
            try (OutputStream out = exchange.getResponseBody()) {
                int limit = cutAfter >= 0 ? Math.min(cutAfter, length) : length;
                out.write(content, from, limit);
                if (limit < length) {
                    cutAfter = -1;
                    out.flush();
                    exchange.close(); // connection dropped mid-file
                    return;
                }
            } catch (Exception ignored) {
                // client went away
            }
        });
        server.start();
    }

    @After
    public void tearDown() {
        server.stop(0);
    }

    String url() {
        return "http://127.0.0.1:" + server.getAddress().getPort() + "/f";
    }

    @Test
    public void downloadsAndVerifies() throws Exception {
        File f = Downloader.fetch(url(), dir, sha, content.length, "apk", 0, null);
        assertArrayEquals(content, Files.readAllBytes(f.toPath()));
        assertEquals(sha + ".apk", f.getName());
    }

    @Test
    public void resumesWhereItStoppedAfterAConnectionDrop() throws Exception {
        cutAfter = 100_000;
        try {
            Downloader.fetch(url(), dir, sha, content.length, "apk", 0, null);
            fail("should report the drop");
        } catch (java.io.IOException expected) {
            // dropped
        }
        File f = Downloader.fetch(url(), dir, sha, content.length, "apk", 0, null);
        assertArrayEquals(content, Files.readAllBytes(f.toPath()));
        assertEquals("bytes=100000-", ranges.get(ranges.size() - 1));
    }

    @Test
    public void startsOverIfTheServerIgnoresTheRange() throws Exception {
        cutAfter = 50_000;
        ignoreRange = true;
        try {
            Downloader.fetch(url(), dir, sha, content.length, "apk", 0, null);
        } catch (java.io.IOException expected) {
            // dropped
        }
        File f = Downloader.fetch(url(), dir, sha, content.length, "apk", 0, null);
        assertArrayEquals(content, Files.readAllBytes(f.toPath()));
    }

    @Test
    public void refusesAFileWithTheWrongHashAndDeletesIt() throws Exception {
        String wrong = "00".repeat(32);
        try {
            Downloader.fetch(url(), dir, wrong, content.length, "apk", 0, null);
            fail("accepted a file with the wrong hash");
        } catch (Downloader.Failed e) {
            assertTrue(e.getMessage().contains("hash"));
        }
        assertFalse(new File(dir, wrong + ".part").exists());
        assertFalse(new File(dir, wrong + ".apk").exists());
    }

    @Test
    public void refusesWhenTheServerSizeDiffersFromTheManifest() throws Exception {
        try {
            Downloader.fetch(url(), dir, sha, content.length + 1, "apk", 0, null);
            fail("accepted a size mismatch");
        } catch (Downloader.Failed expected) {
            // refused before writing
        }
    }

    @Test
    public void refusesPlainHttpFromOtherHosts() throws Exception {
        try {
            Downloader.fetch("http://cdn.example.com/f", dir, sha, 1, "apk", 0, null);
            fail("plain http allowed");
        } catch (Downloader.Failed expected) {
            // https only
        }
    }
}
