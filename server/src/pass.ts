// Game Pass tokens: what the box keeps and checks offline.
//
// Payload: {"type":"tundu.pass","v":1,"device":"d_…","pass_until":<s>,"now":<s>,"blocked":false}
// - pass_until: Game Pass games open while the box's trusted time is before this.
// - now: the server's clock when signed. The box uses it as a trusted time sample, so turning the box's clock
//   back doesn't extend the pass (see the box's PassClock).
// - device: the token only counts on that box.

import { importSigner, signDoc, type SignedDoc, type Signer } from "./crypto.ts";
import type { Env } from "./http.ts";

export const PASS_TYPE = "tundu.pass";

let cached: { secret: string; signer: Promise<Signer> } | null = null;

/** The signing key, imported once per Worker instance. */
export function passSigner(env: Env): Promise<Signer> {
  if (!cached || cached.secret !== env.PASS_SIGNING_KEY) {
    cached = { secret: env.PASS_SIGNING_KEY, signer: importSigner(env.PASS_SIGNING_KEY) };
  }
  return cached.signer;
}

export interface PassState {
  id: string;
  pass_until: number;
  blocked: number;
}

export async function passToken(env: Env, device: PassState, now: number): Promise<SignedDoc> {
  const blocked = device.blocked === 1;
  return signDoc(await passSigner(env), {
    type: PASS_TYPE,
    v: 1,
    device: device.id,
    // A blocked box gets a token that closes Game Pass right away.
    pass_until: blocked ? Math.min(device.pass_until, now) : device.pass_until,
    now,
    blocked,
  });
}
