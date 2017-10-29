﻿using System;

namespace Meziantou.Framework.Win32
{
    public class TokenEntry
    {
        public TokenEntry(SecurityIdentifier sid)
        {
            Sid = sid ?? throw new ArgumentNullException(nameof(sid));
        }

        public SecurityIdentifier Sid { get; }
    }
}
