﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace OpenMLTD.MillionDance.Entities.Pmx {
    public sealed class PmxSoftBody : PmxBaseBody {

        internal PmxSoftBody() {
        }

        public SoftBodyShape Shape { get; internal set; }

        public SoftBodyFlags Flags { get; internal set; }

        public int BendingLinkDistance { get; internal set; }

        public int ClusterCount { get; internal set; }

        public float TotalMass { get; internal set; }

        public float Margin { get; internal set; }

        [NotNull]
        public SoftBodyConfig Config { get; } = new SoftBodyConfig();

        [NotNull]
        public SoftBodyMaterialConfig MaterialConfig { get; } = new SoftBodyMaterialConfig();

        [NotNull, ItemNotNull]
        public BodyAnchor[] BodyAnchors { get; internal set; } = Array.Empty<BodyAnchor>();

        [NotNull, ItemNotNull]
        public VertexPin[] VertexPins { get; internal set; } = Array.Empty<VertexPin>();

    }
}
