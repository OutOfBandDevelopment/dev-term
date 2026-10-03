import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/**
 * Example out-of-process dev-term presenter in Java (single file: java Shout.java): reports the byte count.
 * Protocol (JSON lines on stdin/stdout): see docs/design/proposals/out-of-process-plugins.md.
 * Parses with plain string checks so it needs no JSON library.
 */
public class Shout {
    public static void main(String[] args) throws Exception {
        var in = new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8));
        String line;
        while ((line = in.readLine()) != null) {
            if (line.contains("\"hello\"")) {
                System.out.println("{\"type\":\"hello\",\"name\":\"java-count\",\"protocol\":1}");
            } else if (line.contains("\"render\"")) {
                int start = line.indexOf("\"hex\":\"") + 7;
                String hex = line.substring(start, line.indexOf('"', start));
                System.out.println("{\"type\":\"output\",\"lines\":[\"" + (hex.length() / 2) + " bytes\"]}");
            } else {
                System.out.println("{\"type\":\"output\",\"lines\":[]}");
            }
            System.out.flush();
        }
    }
}
