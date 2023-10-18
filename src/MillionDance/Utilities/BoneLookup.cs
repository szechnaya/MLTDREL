﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using AssetStudio.Extended.CompositeModels;
using AssetStudio.Extended.CompositeModels.Utilities;
using JetBrains.Annotations;
using OpenMLTD.MillionDance.Core;
using OpenMLTD.MillionDance.Entities.Internal;
using OpenMLTD.MillionDance.Entities.Pmx;
using OpenMLTD.MillionDance.Extensions;
using OpenTK;

namespace OpenMLTD.MillionDance.Utilities {
    internal sealed class BoneLookup {

        public BoneLookup([NotNull] ConversionConfig conversionConfig) {
            switch (conversionConfig.SkeletonFormat) {
                case SkeletonFormat.Mmd:
                    _bonePathMap = _bonePathMapMmd;
                    break;
                case SkeletonFormat.Mltd:
                    _bonePathMap = _bonePathMapMltd;
                    break;
                default:
                    throw new NotSupportedException("You must choose a skeleton format.");
            }

            var dict = new Dictionary<string, string>();

            foreach (var kv in _bonePathMap) {
                string boneName;

                if (kv.Key.Contains(BoneNamePart_BodyScale)) {
                    boneName = kv.Key.Replace(BoneNamePart_BodyScale, string.Empty);
                } else {
                    boneName = kv.Key;
                }

                dict.Add(boneName, kv.Value);
            }

            dict.AssertAllValuesUnique();

            _boneNameMap = dict;

            {
                var d = new Dictionary<string, string>();

                foreach (var kv in dict) {
                    d.Add(kv.Value, kv.Key);
                }

                _boneNameMapInversion = d;
            }

            _conversionConfig = conversionConfig;
            _scalingConfig = new ScalingConfig(conversionConfig);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NotNull, ItemNotNull]
        public BoneNode[] BuildBoneHierarchy([NotNull] PrettyAvatar avatar) {
            return BuildBoneHierarchy(avatar, true);
        }

        [NotNull, ItemNotNull]
        private BoneNode[] BuildBoneHierarchy([NotNull] PrettyAvatar avatar, bool fixKubi) {
            var boneList = new List<BoneNode>();
            var skeletonNodes = avatar.AvatarSkeleton.Nodes;
            var scaleUnityToPmx = _scalingConfig.ScaleUnityToPmx;

            for (var i = 0; i < skeletonNodes.Length; i++) {
                var n = skeletonNodes[i];

                var parent = n.ParentIndex >= 0 ? boneList[n.ParentIndex] : null;
                var boneId = avatar.AvatarSkeleton.NodeIDs[i];
                var bonePath = avatar.BoneNamesMap[boneId];

                var boneIndex = avatar.AvatarSkeleton.NodeIDs.FindIndex(boneId);

                if (boneIndex < 0) {
                    throw new IndexOutOfRangeException();
                }

                var initialPose = avatar.AvatarSkeletonPose.Transforms[boneIndex];

                var t = initialPose.LocalPosition.ToOpenTK() * scaleUnityToPmx;
                var q = initialPose.LocalRotation.ToOpenTK();

                var bone = new BoneNode(parent, i, bonePath, t, q);

                boneList.Add(bone);
            }

            PostprocessBoneList(boneList, fixKubi, false);

            return boneList.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [NotNull, ItemNotNull]
        public BoneNode[] BuildBoneHierarchy([NotNull] TransformHierarchies transformHierarchies) {
            return BuildBoneHierarchy(transformHierarchies, true);
        }

        [NotNull, ItemNotNull]
        private BoneNode[] BuildBoneHierarchy([NotNull] TransformHierarchies transformHierarchies, bool fixKubi) {
            var boneList = new List<BoneNode>();

            {
                var bonePathList = new List<string>();
                var orderedTransformHierarchy = transformHierarchies.GetOrderedObjectArray();

                var scaleUnityToPmx = _scalingConfig.ScaleUnityToPmx;

                for (var i = 0; i < orderedTransformHierarchy.Length; i += 1) {
                    var n = orderedTransformHierarchy[i];

                    var bonePath = n.GetFullName();
                    bonePathList.Add(bonePath);

                    var parentPath = bonePath.BreakUntilLast('/');
                    int parentIndex;

                    if (parentPath == bonePath) {
                        // It's a root bone
                        parentIndex = -1;
                    } else {
                        parentIndex = n.Parent != null ? bonePathList.IndexOf(parentPath) : -1;
                    }

                    var parent = parentIndex >= 0 ? boneList[parentIndex] : null;

                    var initialPose = n.Transform;

                    var t = initialPose.LocalPosition.ToOpenTK() * scaleUnityToPmx;
                    var q = initialPose.LocalRotation.ToOpenTK();

                    var bone = new BoneNode(parent, i, bonePath, t, q);

                    boneList.Add(bone);
                }
            }

            PostprocessBoneList(boneList, fixKubi, true);

            return boneList.ToArray();
        }

        private static void PostprocessBoneList([NotNull] List<BoneNode> boneList, bool fixKubi, bool kubiParentIsNull) {
            boneList.AssertAllUnique();

#if DEBUG
            Debug.WriteLine("Model bones:");

            for (var i = 0; i < boneList.Count; i++) {
                var bone = boneList[i];
                Debug.WriteLine($"[{i.ToString()}]: {bone.ToString()}");
            }
#endif

            foreach (var bone in boneList) {
                bone.Initialize();
            }

            if (fixKubi) {
                // Fix "KUBI" (neck) bone's parent
                var kubiParent = boneList.Find(bn => bn.Path == "MODEL_00/BASE/MUNE1/MUNE2/KUBI");

                Debug.Assert(kubiParent != null, nameof(kubiParent) + " != null");

                var kubiBone = boneList.Find(bn => bn.Path == "KUBI");

                Debug.Assert(kubiBone != null, nameof(kubiBone) + " != null");

                kubiParent.AddChild(kubiBone);


                if (kubiParentIsNull) {
                    // In the Transform-based searching approach, the parent of "KUBI" (in the head model) should be always null.
                    Debug.Assert(kubiBone.Parent == null, nameof(kubiBone) + "." + nameof(kubiBone.Parent) + " == null");
                } else {
                    Debug.Assert(kubiBone.Parent != null, nameof(kubiBone) + "." + nameof(kubiBone.Parent) + " != null");

                    // Don't forget to remove it from its old parent (or it will be updated twice from two parents).
                    // The original parents and grandparents of KUBI are not needed; they are just like model anchors and shouldn't be animated.
                    // See decompiled model for more information.
                    kubiBone.Parent.RemoveChild(kubiBone);
                }

                kubiBone.Parent = kubiParent;

                // Set its new initial parameters.
                // Since the two bones (KUBI and MODEL_00/BASE/MUNE1/MUNE2/KUBI) actually share the exact same transforms,
                // set its local transform to identity (t=0, q=0).
                kubiBone.InitialPosition = Vector3.Zero;
                kubiBone.InitialRotation = Quaternion.Identity;
                kubiBone.LocalPosition = Vector3.Zero;
                kubiBone.LocalRotation = Quaternion.Identity;

                foreach (var bone in boneList) {
                    bone.Initialize(true);
                }
            }

            foreach (var bone in boneList) {
                var level = 0;
                var parent = bone.Parent;

                while (parent != null) {
                    ++level;
                    parent = parent.Parent;
                }

                bone.Level = level;
            }
        }

        [NotNull, ItemNotNull]
        public BoneNode[] BuildBoneHierarchy([NotNull] PmxModel pmx) {
            var boneList = new List<BoneNode>();
            var pmxBones = pmx.Bones;
            var pmxBoneCount = pmxBones.Length;

            var boneNameMapInversion = _boneNameMapInversion;

            for (var i = 0; i < pmxBoneCount; i++) {
                var pmxBone = pmxBones[i];
                var parent = pmxBone.ParentIndex >= 0 ? boneList[pmxBone.ParentIndex] : null;

                boneNameMapInversion.TryGetValue(pmx.Name, out var mltdBoneName);

                var path = mltdBoneName ?? pmxBone.Name;

                Vector3 t;

                if (parent != null) {
                    t = pmxBone.InitialPosition - parent.InitialPosition;
                } else {
                    t = pmxBone.InitialPosition;
                }

                var bone = new BoneNode(parent, i, path, t, Quaternion.Identity);

                boneList.Add(bone);
            }

            foreach (var bone in boneList) {
                var level = 0;
                var parent = bone.Parent;

                while (parent != null) {
                    ++level;
                    parent = parent.Parent;
                }

                bone.Level = level;
            }

            foreach (var bone in boneList) {
                bone.Initialize();
            }

            return boneList.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string TranslateBoneName([NotNull] string nameJp, [NotNull] string defaultValue) {
            if (NameJpToEn.ContainsKey(nameJp)) {
                return NameJpToEn[nameJp];
            } else {
                return defaultValue;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string TranslateBoneName([NotNull] string nameJp) {
            return TranslateBoneName(nameJp, string.Empty);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetPmxBoneName([NotNull] string mltdBoneName) {
            return GetBoneNameFromDict(_boneNameMap, mltdBoneName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetVmdBoneNameFromBonePath([NotNull] string mltdBonePath) {
            return GetBoneNameFromDict(_bonePathMap, mltdBonePath);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetVmdBoneNameFromBoneName([NotNull] string mltdBoneName) {
            return GetBoneNameFromDict(_boneNameMap, mltdBoneName);
        }

        [NotNull]
        private string GetBoneNameFromDict([NotNull] Dictionary<string, string> dict, [NotNull] string mltdBoneName) {
            string pmxBoneName;

            if (_conversionConfig.TranslateBoneNamesToMmd) {
                if (dict.ContainsKey(mltdBoneName)) {
                    pmxBoneName = dict[mltdBoneName];
                } else {
                    pmxBoneName = mltdBoneName.BreakLast('/');

                    // Bone names in MLTD are always ASCII characters so we can simply use string length
                    // instead of byte length.
                    if (pmxBoneName.Length > 15) {
                        // Note that here we assume that every bone's name (= name of the game object) is different,
                        // i.e. this can't happen: "a/b/c" and "d/e/c".
                        // Usually we can use `mltdBoneName` (the complete path), but it can fail on some models: e.g. ex001_a + ex001_003mik, where
                        // AddSwayBones() 'correspondingBone != null' assertion fails.
                        var boneHash = pmxBoneName.GetHashCode().ToString("x8");
                        // Prevent the name exceeding max length (15 bytes)
                        pmxBoneName = $"Bone #{boneHash}";
                    }

                    dict.Add(mltdBoneName, pmxBoneName);
                }
            } else {
                pmxBoneName = mltdBoneName.BreakLast('/');
            }

            return pmxBoneName;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNameGenerated([NotNull] string mltdBoneName) {
            foreach (var part in CompilerGeneratedJointParts) {
                if (mltdBoneName.Contains(part)) {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBoneMovable([NotNull] string mltdBoneName) {
            return MovableBoneNames.Contains(mltdBoneName);
        }

        // ReSharper disable once InconsistentNaming
        public const string BoneNamePart_BodyScale = "BODY_SCALE/";

        [NotNull]
        private readonly ConversionConfig _conversionConfig;

        [NotNull]
        private readonly ScalingConfig _scalingConfig;

        [NotNull]
        private readonly Dictionary<string, string> _boneNameMap;

        [NotNull]
        private readonly Dictionary<string, string> _boneNameMapInversion;

        [NotNull]
        private readonly Dictionary<string, string> _bonePathMap;

        [NotNull, ItemNotNull]
        private static readonly HashSet<string> MovableBoneNames = new HashSet<string> {
            "",
            "POSITION",
            "MODEL_00",
            "MODEL_00/BASE"
        };

        [NotNull, ItemNotNull]
        private static readonly HashSet<string> CompilerGeneratedJointParts = new HashSet<string> {
            "__rot",
            "__null",
            "__const",
            "__twist",
            "__slerp"
        };

        [NotNull]
        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        private readonly Dictionary<string, string> _bonePathMapMltd = new Dictionary<string, string> {
            ["POSITION"] = "操作中心",
            ["POSITION/SCALE_POINT"] = "全ての親",
            ["MODEL_00"] = "センター",
            ["MODEL_00/BODY_SCALE/BASE"] = "グルーブ",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI"] = "腰",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L"] = "左足",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L"] = "左ひざ",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L/ASHI_L"] = "左足首",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L/ASHI_L/TSUMASAKI_L"] = "左つま先",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R"] = "右足",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R"] = "右ひざ",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R/ASHI_R"] = "右足首",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R/ASHI_R/TSUMASAKI_R"] = "右つま先",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1"] = "上半身",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2"] = "上半身2",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/KUBI"] = "首",
            //["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/KUBI/ATAMA"] = "頭",
            ["KUBI/ATAMA"] = "頭", // See VmdCreator.BoneAttachmentMap
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L"] = "左肩",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L"] = "左腕",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L"] = "左ひじ",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L"] = "左手首",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L"] = "左人指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L/HITO2_L"] = "左人指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L/HITO2_L/HITO1_L"] = "左人指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L"] = "左ダミー",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L"] = "左小指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L/KO2_L"] = "左小指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L/KO2_L/KO1_L"] = "左小指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L"] = "左薬指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L/KUSU2_L"] = "左薬指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L/KUSU2_L/KUSU1_L"] = "左薬指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L"] = "左中指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L/NAKA2_L"] = "左中指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L/NAKA2_L/NAKA1_L"] = "左中指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L"] = "左親指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L/OYA2_L"] = "左親指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L/OYA2_L/OYA1_L"] = "左親指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R"] = "右肩",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R"] = "右腕",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R"] = "右ひじ",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R"] = "右手首",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R"] = "右人指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R/HITO2_R"] = "右人指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R/HITO2_R/HITO1_R"] = "右人指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R"] = "右ダミー",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R"] = "右小指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R/KO2_R"] = "右小指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R/KO2_R/KO1_R"] = "右小指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R"] = "右薬指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R/KUSU2_R"] = "右薬指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R/KUSU2_R/KUSU1_R"] = "右薬指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R"] = "右中指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R/NAKA2_R"] = "右中指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R/NAKA2_R/NAKA1_R"] = "右中指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R"] = "右親指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R/OYA2_R"] = "右親指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R/OYA2_R/OYA1_R"] = "右親指３"
        };

        [NotNull]
        [SuppressMessage("ReSharper", "StringLiteralTypo")]
        private readonly Dictionary<string, string> _bonePathMapMmd = new Dictionary<string, string> {
            [""] = "操作中心",
            // We can't keep this; they will cause compatibility issues when we manually fix the master and center bones.
            //["POSITION"] = "全ての親",
            //["POSITION/SCALE_POINT"] = "センター",
            ["MODEL_00"] = "グルーブ",
            ["MODEL_00/BODY_SCALE/BASE"] = "腰",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI"] = "下半身",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L"] = "左足",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L"] = "左ひざ",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L/ASHI_L"] = "左足首",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_L/HIZA_L/ASHI_L/TSUMASAKI_L"] = "左つま先",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R"] = "右足",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R"] = "右ひざ",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R/ASHI_R"] = "右足首",
            ["MODEL_00/BODY_SCALE/BASE/KOSHI/MOMO_R/HIZA_R/ASHI_R/TSUMASAKI_R"] = "右つま先",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1"] = "上半身",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2"] = "上半身2",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/KUBI"] = "首",
            //["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/KUBI/ATAMA"] = "頭",
            ["KUBI/ATAMA"] = "頭", // See VmdCreator.BoneAttachmentMap
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L"] = "左肩",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L"] = "左腕",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L"] = "左ひじ",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L"] = "左手首",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L"] = "左人指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L/HITO2_L"] = "左人指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/HITO3_L/HITO2_L/HITO1_L"] = "左人指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L"] = "左ダミー",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L"] = "左小指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L/KO2_L"] = "左小指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KO3_L/KO2_L/KO1_L"] = "左小指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L"] = "左薬指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L/KUSU2_L"] = "左薬指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/KUKO_L/KUSU3_L/KUSU2_L/KUSU1_L"] = "左薬指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L"] = "左中指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L/NAKA2_L"] = "左中指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/NAKA3_L/NAKA2_L/NAKA1_L"] = "左中指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L"] = "左親指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L/OYA2_L"] = "左親指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_L/KATA_L/UDE_L/TE_L/OYA3_L/OYA2_L/OYA1_L"] = "左親指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R"] = "右肩",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R"] = "右腕",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R"] = "右ひじ",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R"] = "右手首",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R"] = "右人指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R/HITO2_R"] = "右人指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/HITO3_R/HITO2_R/HITO1_R"] = "右人指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R"] = "右ダミー",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R"] = "右小指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R/KO2_R"] = "右小指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KO3_R/KO2_R/KO1_R"] = "右小指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R"] = "右薬指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R/KUSU2_R"] = "右薬指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/KUKO_R/KUSU3_R/KUSU2_R/KUSU1_R"] = "右薬指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R"] = "右中指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R/NAKA2_R"] = "右中指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/NAKA3_R/NAKA2_R/NAKA1_R"] = "右中指３",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R"] = "右親指１",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R/OYA2_R"] = "右親指２",
            ["MODEL_00/BODY_SCALE/BASE/MUNE1/MUNE2/SAKOTSU_R/KATA_R/UDE_R/TE_R/OYA3_R/OYA2_R/OYA1_R"] = "右親指３"
        };

        [NotNull]
        private static readonly Dictionary<string, string> NameJpToEn = new Dictionary<string, string> {
            ["操作中心"] = "view cnt",
            ["全ての親"] = "master",
            ["センター"] = "center",
            ["グルーブ"] = "groove",
            ["腰"] = "waist",
            ["左足"] = "leg_L",
            ["左ひざ"] = "knee_L",
            ["左足首"] = "angle_L",
            ["左つま先"] = "toe_L",
            ["右足"] = "leg_R",
            ["右ひざ"] = "knee_R",
            ["右足首"] = "ankle_R",
            ["右つま先"] = "toe_R",
            ["上半身"] = "upper body",
            ["上半身2"] = "upper body2",
            ["首"] = "neck",
            ["頭"] = "head",
            ["左肩"] = "shoulder_L",
            ["左腕"] = "arm_L",
            ["左ひじ"] = "elbow_L",
            ["左手首"] = "wrist_L",
            ["左人指１"] = "fore1_L",
            ["左人指２"] = "fore2_L",
            ["左人指３"] = "fore3_L",
            ["左ダミー"] = "dummy_L",
            ["左小指１"] = "little1_L",
            ["左小指２"] = "little2_L",
            ["左小指３"] = "little3_L",
            ["左薬指１"] = "third1_L",
            ["左薬指２"] = "third2_L",
            ["左薬指３"] = "third3_L",
            ["左中指１"] = "middle1_L",
            ["左中指２"] = "middle2_L",
            ["左中指３"] = "middle3_L",
            ["左親指１"] = "thumb1_L",
            ["左親指２"] = "thumb2_L",
            ["左親指３"] = "thumb3_L",
            ["右肩"] = "shoulder_R",
            ["右腕"] = "arm_R",
            ["右ひじ"] = "elbow_R",
            ["右手首"] = "wrist_R",
            ["右人指１"] = "fore1_R",
            ["右人指２"] = "fore2_R",
            ["右人指３"] = "fore3_R",
            ["右ダミー"] = "dummy_R",
            ["右小指１"] = "little1_R",
            ["右小指２"] = "little2_R",
            ["右小指３"] = "little3_R",
            ["右薬指１"] = "third1_R",
            ["右薬指２"] = "third2_R",
            ["右薬指３"] = "third3_R",
            ["右中指１"] = "middle1_R",
            ["右中指２"] = "middle2_R",
            ["右中指３"] = "middle3_R",
            ["右親指１"] = "thumb1_R",
            ["右親指２"] = "thumb2_R",
            ["右親指３"] = "thumb3_R",
            ["下半身"] = "lower body",
            ["左足IK親"] = "leg IKP_L",
            ["左足ＩＫ"] = "leg IK_L",
            ["左つま先ＩＫ"] = "toe IK_L",
            ["右足IK親"] = "leg IKP_R",
            ["右足ＩＫ"] = "leg IK_R",
            ["右つま先ＩＫ"] = "toe IK_R",
        };

    }
}
