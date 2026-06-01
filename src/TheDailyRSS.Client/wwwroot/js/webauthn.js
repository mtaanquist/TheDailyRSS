// WebAuthn/FIDO2 bridge for passkeys (#38). Blazor hands us the server's options JSON (which uses
// base64url for binary fields); we convert those to ArrayBuffers, run the browser ceremony, then return
// the credential as JSON with binary fields back in base64url — exactly what the FIDO2 library expects.
(function () {
    function b64urlToBuf(s) {
        s = s.replace(/-/g, '+').replace(/_/g, '/');
        const pad = s.length % 4;
        if (pad) s += '='.repeat(4 - pad);
        const bin = atob(s);
        const bytes = new Uint8Array(bin.length);
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes.buffer;
    }

    function bufToB64url(buf) {
        const bytes = new Uint8Array(buf);
        let bin = '';
        for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    }

    async function register(optionsJson) {
        const o = JSON.parse(optionsJson);
        o.challenge = b64urlToBuf(o.challenge);
        o.user.id = b64urlToBuf(o.user.id);
        (o.excludeCredentials || []).forEach(c => { c.id = b64urlToBuf(c.id); });

        const cred = await navigator.credentials.create({ publicKey: o });
        return JSON.stringify({
            id: cred.id,
            rawId: bufToB64url(cred.rawId),
            type: cred.type,
            extensions: cred.getClientExtensionResults(),
            response: {
                attestationObject: bufToB64url(cred.response.attestationObject),
                clientDataJSON: bufToB64url(cred.response.clientDataJSON),
            },
        });
    }

    async function authenticate(optionsJson) {
        const o = JSON.parse(optionsJson);
        o.challenge = b64urlToBuf(o.challenge);
        (o.allowCredentials || []).forEach(c => { c.id = b64urlToBuf(c.id); });

        const cred = await navigator.credentials.get({ publicKey: o });
        return JSON.stringify({
            id: cred.id,
            rawId: bufToB64url(cred.rawId),
            type: cred.type,
            extensions: cred.getClientExtensionResults(),
            response: {
                authenticatorData: bufToB64url(cred.response.authenticatorData),
                clientDataJSON: bufToB64url(cred.response.clientDataJSON),
                signature: bufToB64url(cred.response.signature),
                userHandle: cred.response.userHandle ? bufToB64url(cred.response.userHandle) : null,
            },
        });
    }

    // True when the browser supports WebAuthn at all (so the UI can hide passkey affordances otherwise).
    function available() {
        return !!(window.PublicKeyCredential && navigator.credentials && navigator.credentials.create);
    }

    window.tdrPasskey = { register, authenticate, available };
})();
