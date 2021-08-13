﻿using System.Text.Json.Serialization;

namespace Meziantou.Framework.ChromiumTracing;

public sealed class ChromiumTracingObjectSnapshotEvent : ChromiumTracingObjectEvent
{
    [JsonPropertyName("ph")]
    public override string Type => "O";
}
