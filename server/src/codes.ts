// Gift card codes: 16 characters, shown as XXXX-XXXX-XXXX-XXXX.
//
// - 15 random characters from a 32-letter alphabet (75 bits from a CSPRNG) + 1 check character (Luhn mod 32),
//   so a typo is caught on the box before anything is sent.
// - The alphabet leaves out 0, 1, I and O, which are easy to confuse on a card or a TV screen.
// - The code carries no meaning (no batch, no value): that lives only in the database.
// - The server never stores codes: only HMAC-SHA256(pepper, code). The pepper is a Worker secret, so a copy of
//   the database alone is useless for finding codes.
//
// The same rules are implemented on the box (Java GiftCode) and in Unity (C# GiftCode); all three are tested
// against test/vectors/codes.json.

export const ALPHABET = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
export const CODE_LENGTH = 16;
/** QR codes printed on the cards hold this URL. The code is in the fragment, which browsers never send to a server. */
export const QR_PREFIX = "https://tundu.al/kod#";

const INDEX: Record<string, number> = Object.fromEntries([...ALPHABET].map((c, i) => [c, i]));

/** Luhn mod N check character for the given characters (all from ALPHABET). */
export function checkChar(body: string): string {
  const n = ALPHABET.length;
  let factor = 2;
  let sum = 0;
  for (let i = body.length - 1; i >= 0; i--) {
    const value = INDEX[body[i]!];
    if (value === undefined) throw new Error("character outside the alphabet");
    let addend = factor * value;
    factor = factor === 2 ? 1 : 2;
    addend = Math.floor(addend / n) + (addend % n);
    sum += addend;
  }
  return ALPHABET[(n - (sum % n)) % n]!;
}

/** True if the 16 characters are from the alphabet and the last one is the right check character. */
export function isValidCode(code: string): boolean {
  if (code.length !== CODE_LENGTH) return false;
  for (const c of code) if (INDEX[c] === undefined) return false;
  return checkChar(code.slice(0, -1)) === code[CODE_LENGTH - 1];
}

/**
 * Turns what a person typed or a QR code held into the 16-character code, or null if it can't be one.
 * Accepts lower case, spaces, dashes and dots, and the QR URL form.
 */
export function normalizeCode(input: string): string | null {
  if (typeof input !== "string" || input.length > 100) return null;
  let text = input.trim();
  const hash = text.lastIndexOf("#");
  if (hash >= 0) text = text.slice(hash + 1);
  text = text.toUpperCase().replace(/[\s\-.]/g, "");
  return isValidCode(text) ? text : null;
}

export function formatCode(code: string): string {
  return code.match(/.{1,4}/g)!.join("-");
}

/** A new random code (15 random characters + check character). 32 divides 256, so `byte & 31` is uniform. */
export function generateCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH - 1));
  let body = "";
  for (const b of bytes) body += ALPHABET[b & 31];
  return body + checkChar(body);
}

/** Random string from ALPHABET (5 bits per character), for device ids. */
export function randomId(length: number): string {
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  let out = "";
  for (const b of bytes) out += ALPHABET[b & 31];
  return out;
}

const encoder = new TextEncoder();

export async function importPepper(pepperBase64: string): Promise<CryptoKey> {
  const raw = Uint8Array.from(atob(pepperBase64), (c) => c.charCodeAt(0));
  if (raw.length < 32) throw new Error("CODE_PEPPER must be at least 32 random bytes (base64)");
  return crypto.subtle.importKey("raw", raw, { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
}

/** Hex HMAC-SHA256 of the normalized code: the only form in which codes are stored. */
export async function codeHash(pepper: CryptoKey, code: string): Promise<string> {
  const mac = await crypto.subtle.sign("HMAC", pepper, encoder.encode(code));
  return toHex(new Uint8Array(mac));
}

export function toHex(bytes: Uint8Array): string {
  let s = "";
  for (const b of bytes) s += b.toString(16).padStart(2, "0");
  return s;
}
