namespace MoveIt.Games
{
    /// <summary>
    /// Gift card codes: 16 characters (15 random + a Luhn mod 32 check character) from an alphabet without 0, 1, I
    /// and O, shown as XXXX-XXXX-XXXX-XXXX. Same rules as the server (server/src/codes.ts) and the box's Java
    /// (GiftCode.java); all three are tested against server/test/vectors/codes.json. The code-entry screen uses it to
    /// say "a character is wrong" at once, without asking the server.
    /// </summary>
    public static class GiftCode
    {
        public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        public const int Length = 16;

        public static char CheckChar(string body)
        {
            int n = Alphabet.Length;
            int factor = 2;
            int sum = 0;
            for (int i = body.Length - 1; i >= 0; i--)
            {
                int value = Alphabet.IndexOf(body[i]);
                if (value < 0) throw new System.ArgumentException("character outside the alphabet");
                int addend = factor * value;
                factor = factor == 2 ? 1 : 2;
                addend = addend / n + addend % n;
                sum += addend;
            }
            return Alphabet[(n - sum % n) % n];
        }

        public static bool IsValid(string code)
        {
            if (code == null || code.Length != Length) return false;
            foreach (char c in code)
                if (Alphabet.IndexOf(c) < 0) return false;
            return CheckChar(code.Substring(0, Length - 1)) == code[Length - 1];
        }

        /// <summary>The 16-character code from typed text or a QR code (https://tundu.al/kod#XXXX-…), or null.</summary>
        public static string Normalize(string input)
        {
            if (input == null || input.Length > 100) return null;
            string text = input.Trim();
            int hash = text.LastIndexOf('#');
            if (hash >= 0) text = text.Substring(hash + 1);
            var sb = new System.Text.StringBuilder(Length);
            foreach (char c in text)
            {
                if (c == '-' || c == '.' || char.IsWhiteSpace(c)) continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            string code = sb.ToString();
            return IsValid(code) ? code : null;
        }

        /// <summary>Groups of four with dashes; works for partial codes too (while typing).</summary>
        public static string Format(string code)
        {
            var sb = new System.Text.StringBuilder(19);
            for (int i = 0; i < code.Length; i++)
            {
                if (i > 0 && i % 4 == 0) sb.Append('-');
                sb.Append(code[i]);
            }
            return sb.ToString();
        }
    }
}
