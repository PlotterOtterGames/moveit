// Throwaway release (update-signing) key for the tests ONLY: public, never trusted by a real build.

/** Private JWK of the test release key (kid "tr1"). */
export const TEST_RELEASE_JWK = {"kty":"EC","x":"ba5DWyyDsOulSKF8exkryM4mQYotWfF-wEdCpLRH_GU","y":"QGYKmUqoNZ_QnXnXfhFcln2Q4QVTKDFXqG1itbsYqz0","crv":"P-256","d":"dCyPw1AMXAwMIVnJ1REz72mqQA36Z3nyKcq08HY6l88","kid":"tr1"};

export const TEST_RELEASE_SPKI = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEba5DWyyDsOulSKF8exkryM4mQYotWfF+wEdCpLRH/GVAZgqZSqg1n9Cdedd+EVyWfZDhBVMoMVeobWK1uxirPQ==";
