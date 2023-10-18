﻿using JetBrains.Annotations;
using OpenTK;

namespace OpenMLTD.MillionDance.Entities.Pmx {
    public sealed class PmxMaterial : IPmxNamedObject {

        internal PmxMaterial() {
        }

        public string Name { get; internal set; } = string.Empty;

        public string NameEnglish { get; internal set; } = string.Empty;

        [NotNull]
        public string TextureFileName { get; internal set; } = string.Empty;

        [NotNull]
        public string SphereTextureFileName { get; internal set; } = string.Empty;

        [NotNull]
        public string ToonTextureFileName { get; internal set; } = string.Empty;

        [NotNull]
        public string MemoTextureFileName { get; internal set; } = string.Empty;

        public Vector4 Diffuse { get; internal set; }

        public Vector4 EdgeColor { get; internal set; }

        public Vector3 Specular { get; internal set; }

        public Vector3 Ambient { get; internal set; }

        public float SpecularPower { get; internal set; }

        public float EdgeSize { get; internal set; } = 1;

        public MaterialFlags Flags { get; internal set; }

        public SphereTextureMode SphereTextureMode { get; internal set; }

        public int AppliedFaceVertexCount { get; internal set; }

    }
}
