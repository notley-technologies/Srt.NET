// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

namespace Srt.Net;

/// <summary>Encryption key exchange state for one direction (<c>SRT_KM_STATE</c>).</summary>
public enum SrtKeyMaterialState
{
    /// <summary>The stream is not encrypted (<c>SRT_KM_S_UNSECURED</c>).</summary>
    Unsecured = 0,

    /// <summary>Keys are being exchanged (<c>SRT_KM_S_SECURING</c>).</summary>
    Securing = 1,

    /// <summary>Keys were exchanged and decryption works (<c>SRT_KM_S_SECURED</c>).</summary>
    Secured = 2,

    /// <summary>The peer encrypts but no passphrase is configured locally (<c>SRT_KM_S_NOSECRET</c>).</summary>
    NoSecret = 3,

    /// <summary>The passphrases do not match (<c>SRT_KM_S_BADSECRET</c>).</summary>
    BadSecret = 4,

    /// <summary>The cryptographic modes do not match (<c>SRT_KM_S_BADCRYPTOMODE</c>).</summary>
    BadCryptoMode = 5
}
