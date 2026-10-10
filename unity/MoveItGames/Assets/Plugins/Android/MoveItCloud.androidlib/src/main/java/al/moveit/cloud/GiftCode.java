package al.moveit.cloud;

/**
 * Gift card codes: 16 characters (15 random + a Luhn mod 32 check character) from an alphabet without 0, 1, I, O.
 * Same rules as server/src/codes.ts and the C# GiftCode; all three are tested against
 * server/test/vectors/codes.json. Checking the check character here means a typo is caught on the TV at once,
 * without a network call and without counting as a wrong code on the server.
 */
public final class GiftCode {
    public static final String ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    public static final int LENGTH = 16;

    private GiftCode() {}

    /** Check character for characters that are all in ALPHABET. */
    public static char checkChar(String body) {
        int n = ALPHABET.length();
        int factor = 2;
        int sum = 0;
        for (int i = body.length() - 1; i >= 0; i--) {
            int value = ALPHABET.indexOf(body.charAt(i));
            if (value < 0) throw new IllegalArgumentException("character outside the alphabet");
            int addend = factor * value;
            factor = factor == 2 ? 1 : 2;
            addend = addend / n + addend % n;
            sum += addend;
        }
        return ALPHABET.charAt((n - sum % n) % n);
    }

    public static boolean isValid(String code) {
        if (code == null || code.length() != LENGTH) return false;
        for (int i = 0; i < LENGTH; i++) if (ALPHABET.indexOf(code.charAt(i)) < 0) return false;
        return checkChar(code.substring(0, LENGTH - 1)) == code.charAt(LENGTH - 1);
    }

    /** The 16-character code from typed text or a QR code (https://tundu.al/kod#XXXX-…), or null. */
    public static String normalize(String input) {
        if (input == null || input.length() > 100) return null;
        String text = input.trim();
        int hash = text.lastIndexOf('#');
        if (hash >= 0) text = text.substring(hash + 1);
        StringBuilder sb = new StringBuilder(LENGTH);
        for (int i = 0; i < text.length(); i++) {
            char c = text.charAt(i);
            if (c == '-' || c == '.' || Character.isWhitespace(c) || Character.isSpaceChar(c)) continue;
            sb.append(Character.toUpperCase(c));
        }
        String code = sb.toString();
        return isValid(code) ? code : null;
    }

    public static String format(String code) {
        return code.substring(0, 4) + "-" + code.substring(4, 8) + "-" + code.substring(8, 12) + "-" + code.substring(12);
    }
}
