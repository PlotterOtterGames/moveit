// Throwaway keys and secrets for the tests ONLY. They are public (committed) and no build ever trusts them:
// the box's pinned keys live in TrustedKeys.java and the real secrets only in Cloudflare (wrangler secret put).

/** Private JWK of the test Game Pass signing key (kid "t1"). */
export const TEST_PASS_JWK = {"kty": "EC", "x": "sq8W2VsfauPlaIRW59LnMZ4zXaRagef47XX3EhAFkHg", "y": "GK8OXiAsV3VJhomFVKZJ324CtzKLaxERAWVFtghQiG4", "crv": "P-256", "d": "ZsEnp67qYLbcrJgucPbDolOG0bRnUBmW4BHxLP-24i4", "kid": "t1"};

/** Its public key, SPKI DER in base64 (what the box would pin). */
export const TEST_PASS_SPKI = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEsq8W2VsfauPlaIRW59LnMZ4zXaRagef47XX3EhAFkHgYrw5eICxXdUmGiYVUpknfbgK3MotrEREBZUW2CFCIbg==";

/** The test admin token (the server only knows its SHA-256). */
export const TEST_ADMIN_TOKEN = "kc8M0ZRcJXOV7iM5mfOmU8IrRyYrCZgoq8CPp-yEkrE";

export const TEST_SECRETS = {
  PASS_SIGNING_KEY: JSON.stringify(TEST_PASS_JWK),
  CODE_PEPPER: "V2Dx4NxP185Eqj+Wvig9c8uepL+O8p+VXacI1iPLxbQ=",
  ADMIN_TOKEN_SHA256: "873dfe33b2bfafc4dd732265cce315631a6abee2992d143804d56c910a35658a",
};
