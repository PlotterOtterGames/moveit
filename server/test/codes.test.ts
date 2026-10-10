import { describe, expect, it } from "vitest";
import { ALPHABET, checkChar, codeHash, formatCode, generateCode, importPepper, isValidCode, normalizeCode, QR_PREFIX } from "../src/codes.ts";
import vectors from "./vectors/codes.json";

describe("gift card codes", () => {
  it("generates 16 characters from the alphabet with a valid check character", () => {
    const seen = new Set<string>();
    for (let i = 0; i < 2000; i++) {
      const code = generateCode();
      expect(code).toMatch(/^[2-9A-HJ-NP-Z]{16}$/);
      expect(isValidCode(code)).toBe(true);
      seen.add(code);
    }
    expect(seen.size).toBe(2000);
  });

  it("uses every character about equally (no modulo bias)", () => {
    const counts = new Map<string, number>();
    for (let i = 0; i < 4000; i++) for (const c of generateCode().slice(0, 15)) counts.set(c, (counts.get(c) ?? 0) + 1);
    expect(counts.size).toBe(32);
    const expected = (4000 * 15) / 32;
    for (const n of counts.values()) expect(Math.abs(n - expected) / expected).toBeLessThan(0.12);
  });

  it("catches every single-character typo", () => {
    for (let k = 0; k < 50; k++) {
      const code = generateCode();
      for (let i = 0; i < 16; i++) {
        for (const c of ALPHABET) {
          if (c === code[i]) continue;
          expect(isValidCode(code.slice(0, i) + c + code.slice(i + 1))).toBe(false);
        }
      }
    }
  });

  it("catches swapped neighbours almost always", () => {
    let caught = 0;
    let swaps = 0;
    for (let k = 0; k < 300; k++) {
      const code = generateCode();
      for (let i = 0; i < 15; i++) {
        if (code[i] === code[i + 1]) continue;
        swaps++;
        if (!isValidCode(code.slice(0, i) + code[i + 1] + code[i] + code.slice(i + 2))) caught++;
      }
    }
    expect(caught / swaps).toBeGreaterThan(0.99);
  });

  it("accepts what people type and what the QR code holds", () => {
    const code = generateCode();
    const pretty = formatCode(code);
    expect(pretty).toMatch(/^.{4}-.{4}-.{4}-.{4}$/);
    expect(normalizeCode(pretty)).toBe(code);
    expect(normalizeCode(pretty.toLowerCase())).toBe(code);
    expect(normalizeCode(" " + pretty.replace(/-/g, " ") + " ")).toBe(code);
    expect(normalizeCode(QR_PREFIX + pretty)).toBe(code);
    expect(normalizeCode(code.slice(0, 15))).toBeNull();
    expect(normalizeCode(code + "2")).toBeNull();
    expect(normalizeCode("O" + code.slice(1))).toBeNull(); // O, 0, I, 1 are not in the alphabet
    expect(normalizeCode("x".repeat(500))).toBeNull();
  });

  it("matches the shared test vectors (Java and C# test the same file)", () => {
    expect(vectors.alphabet).toBe(ALPHABET);
    for (const v of vectors.valid) {
      expect(normalizeCode(v.input)).toBe(v.code);
      expect(checkChar(v.code.slice(0, 15))).toBe(v.code[15]);
    }
    for (const input of vectors.invalid) expect(normalizeCode(input)).toBeNull();
  });

  it("hashes with the pepper: same code same hash, different pepper different hash", async () => {
    const a = await importPepper(btoa("a".repeat(32)));
    const b = await importPepper(btoa("b".repeat(32)));
    const code = generateCode();
    expect(await codeHash(a, code)).toBe(await codeHash(a, code));
    expect(await codeHash(a, code)).not.toBe(await codeHash(b, code));
    expect(await codeHash(a, code)).toMatch(/^[0-9a-f]{64}$/);
    await expect(importPepper(btoa("short"))).rejects.toThrow();
  });
});
