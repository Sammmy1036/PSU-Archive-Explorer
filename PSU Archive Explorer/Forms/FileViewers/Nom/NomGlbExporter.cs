using System;
using System.Collections.Generic;
using System.Numerics;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Models;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace psu_archive_explorer.Exporters
{
    /// <summary>
    /// Exports a <see cref="NomFile"/> to a GLB (glTF 2.0 binary) file using SharpGLTF.
    ///
    /// Two modes:
    ///   1) Without a skeleton: emits the animation curves on a *synthetic flat
    ///      skeleton* — every bone parented per a hardcoded standard PSU humanoid
    ///      hierarchy, all bones at origin. Animation data is correct; the rest
    ///      pose is fake. Useful when the user just wants the animation curves
    ///      and will retarget onto an existing rig.
    ///   2) With an <see cref="XnjFile"/> skeleton: emits the animation curves
    ///      on the proper PSU bind pose taken from the XNJ. The exported GLB
    ///      will have a real humanoid armature with correct bone lengths,
    ///      orientations, and proportions.
    ///
    /// Why GLB instead of FBX: Blender's FBX importer doesn't accept ASCII FBX,
    /// and writing binary FBX from scratch is a substantial undertaking with no
    /// official spec. GLB is the Khronos open standard, fully documented, and
    /// imports natively into Blender, Maya, Unity, Unreal, and basically every
    /// modern DCC tool. SharpGLTF handles the binary container details for us.
    /// </summary>
    public static class NomGlbExporter
    {
        /// <summary>
        /// Fallback PSU humanoid bone parenting, used when no XNJ skeleton is
        /// provided. Values updated to match what real PSU XNJ files actually
        /// contain (e.g. Belly is parented to Spine, not Pelvis; clavicles
        /// hang off Neck_root, not Spine1). Anything not in this map gets
        /// parented to Root as a safe fallback.
        /// </summary>
        private static readonly Dictionary<string, string> StandardParents =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Root",       null      },
                { "Navel",      "Root"    },
                { "Pelvis",     "Navel"   },
                { "L_thigh",    "Pelvis"  },
                { "L_calf",     "L_thigh" },
                { "L_foot",     "L_calf"  },
                { "R_thigh",    "Pelvis"  },
                { "R_calf",     "R_thigh" },
                { "R_foot",     "R_calf"  },
                { "Spine",      "Navel"   },
                { "Spine1",     "Spine"   },
                { "Neck_root",  "Spine1"  },
                { "Neck",       "Neck_root" },
                { "Head",       "Neck"    },
                { "L_clavicle", "Neck_root" },   // corrected per XNJ data
                { "L_upperarm", "L_clavicle" },
                { "L_forearm",  "L_upperarm" },
                { "L_hand",     "L_forearm"  },
                { "L_weapon",   "L_hand"  },
                { "R_clavicle", "Neck_root" },   // corrected per XNJ data
                { "R_upperarm", "R_clavicle" },
                { "R_forearm",  "R_upperarm" },
                { "R_hand",     "R_forearm"  },
                { "R_weapon",   "R_hand"  },
                { "L_breast",   "Spine1"  },
                { "R_breast",   "Spine1"  },
                { "Belly",      "Spine"   },     // corrected per XNJ data
                { "Body",       "Root"    },
            };

        /// <summary>
        /// Write a GLB representing the given NOM's animation.
        /// Throws on validation or I/O failure. Returns the path written.
        /// </summary>
        /// <param name="nom">The animation to export. Required.</param>
        /// <param name="outputPath">Output .glb path. Required.</param>
        /// <param name="skeleton">Optional XNJ skeleton. When provided, the
        ///   exported armature uses its real bind pose (bone offsets, rotations,
        ///   and parent hierarchy from the XNJ). When null, falls back to a
        ///   synthetic flat skeleton — all bones at origin, parented per the
        ///   <see cref="StandardParents"/> map.</param>
        public static string Export(NomFile nom, string outputPath, XnjFile skeleton = null)
        {
            if (nom == null) throw new ArgumentNullException(nameof(nom));
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("Output path required.", nameof(outputPath));

            if (nom.boneNames == null || nom.boneNames.Length == 0)
                throw new InvalidOperationException("NOM has no bone names; nothing to export.");
            if (nom.frameCount == 0)
                throw new InvalidOperationException("NOM has frameCount = 0; nothing to animate.");
            if (nom.frameRate <= 0.0f)
                throw new InvalidOperationException("NOM has invalid frameRate (" + nom.frameRate + ").");

            // Decide whether we'll use an XNJ-derived skeleton or the
            // synthetic fallback. We only use the XNJ if its bone count
            // matches the NOM's — a mismatch means the user paired a NOM
            // with a skeleton from a different character type, and silently
            // accepting that would produce a broken export.
            bool useXnj = skeleton != null
                       && skeleton.Bones != null
                       && skeleton.Bones.Count == nom.boneNames.Length;

            // Build the bone hierarchy first. SharpGLTF's NodeBuilder owns
            // both the parent/child structure AND the animation curves — each
            // node holds its own per-track keyframes.
            var nodes = new NodeBuilder[nom.boneNames.Length];
            NodeBuilder root = null;

            // First pass: create a NodeBuilder for each bone, no parenting yet.
            // We need them all to exist before we can wire up parent relationships,
            // since the parent might come later in the bone list.
            for (int i = 0; i < nom.boneNames.Length; i++)
            {
                string name = string.IsNullOrEmpty(nom.boneNames[i]) ? ("Bone" + i) : nom.boneNames[i];
                nodes[i] = new NodeBuilder(name);

                // If we have an XNJ skeleton, apply its rest-pose transform
                // to this node now. SharpGLTF stores per-node local transforms
                // that are baked into the glTF as the rest-pose values; any
                // animation curve we add later overlays on top of these.
                //
                // XnjBone exposes plain floats (PSULib stays free of the
                // System.Numerics dependency), so we compose the Vector3 /
                // Quaternion here, in the app project, where System.Numerics
                // is already available via SharpGLTF.
                if (useXnj)
                {
                    var xb = skeleton.Bones[i];

                    var translation = new Vector3(
                        xb.LocalTranslationX, xb.LocalTranslationY, xb.LocalTranslationZ);
                    var scale = new Vector3(
                        xb.LocalScaleX, xb.LocalScaleY, xb.LocalScaleZ);
                    var rotation = EulerXzyToQuaternion(
                        xb.LocalRotationRadX, xb.LocalRotationRadY, xb.LocalRotationRadZ);

                    nodes[i].LocalTransform = new SharpGLTF.Transforms.AffineTransform(
                        scale, rotation, translation);
                }
            }

            // Second pass: attach each non-root bone to its parent.
            // When XNJ is available, use its explicit parent indices (most
            // accurate). Otherwise fall back to name-based lookup against the
            // hardcoded StandardParents map.
            if (useXnj)
            {
                for (int i = 0; i < nom.boneNames.Length; i++)
                {
                    int parentIdx = skeleton.Bones[i].ParentIndex;
                    if (parentIdx < 0 || parentIdx >= nodes.Length)
                    {
                        // Root bone: remember which one it is and don't reparent.
                        // PSU skeletons can have multiple "roots" (Body, R_weapon,
                        // L_weapon, R_breast, Neck_root, etc. all show ParentIndex = -1
                        // in real XNJ data). We treat the first one we see as THE
                        // root; any others get reattached to it later.
                        if (root == null) root = nodes[i];
                        continue;
                    }
                    nodes[parentIdx].AddNode(nodes[i]);
                }
            }
            else
            {
                // Fallback: name-based parenting.
                var byName = new Dictionary<string, NodeBuilder>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < nom.boneNames.Length; i++)
                {
                    byName[nodes[i].Name] = nodes[i];
                }

                for (int i = 0; i < nom.boneNames.Length; i++)
                {
                    string n = nodes[i].Name;
                    if (string.Equals(n, "Root", StringComparison.OrdinalIgnoreCase))
                    {
                        root = nodes[i];
                        continue;
                    }

                    if (!StandardParents.TryGetValue(n, out string parentName) || parentName == null)
                        parentName = "Root";

                    if (byName.TryGetValue(parentName, out NodeBuilder parentNode))
                        parentNode.AddNode(nodes[i]);
                }
            }

            // If there was no bone literally named "Root", treat bone 0 as
            // the root. PSU skeletons always have "Root" at index 0 in
            // practice, but defending against the unusual case is cheap.
            if (root == null) root = nodes[0];

            // Catch any orphan nodes (parent lookup failed, or XNJ marked
            // them as additional roots) and reattach them to THE root. This
            // keeps the scene valid even if the bone list has unusual entries.
            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == root) continue;
                if (nodes[i].Parent == null)
                    root.AddNode(nodes[i]);
            }

            // Now apply animation curves to each bone.
            // SharpGLTF accepts (time_in_seconds, value) tuples per channel.
            // Track name "Take 001" matches the convention used by FBX exports,
            // which Blender displays in the Action editor.
            const string trackName = "Take 001";

            for (int i = 0; i < nom.boneNames.Length; i++)
            {
                ApplyRotationAnimation(nodes[i], i, nom, trackName);
                ApplyTranslationAnimation(nodes[i], i, nom, trackName);
            }

            // Build the scene. SceneBuilder is SharpGLTF's high-level wrapper
            // that turns NodeBuilder trees + meshes/skins into a glTF document.
            // For animation-only export we have no meshes — just the armature.
            var scene = new SceneBuilder();
            scene.AddNode(root);

            // Serialize.
            var model = scene.ToGltf2();
            model.SaveGLB(outputPath);
            return outputPath;
        }

        /// <summary>
        /// Read rotation keyframes from the NOM's rotationFrameList[boneIndex]
        /// and apply them to the node as a quaternion animation track. Returns
        /// silently if there's no animation data for this bone.
        ///
        /// NomFile encodes rotation frames in tagged "NomFrame" entries. The type
        /// table from NomFile.ReadNomList:
        ///   0x0 -> 4 values (quaternion X,Y,Z,W)        ← actual key
        ///   0x5/6/7 -> 2 values (interpolation hints)   ← skipped in Option A
        ///   0x8..0xB -> 0 values (reset markers)        ← treated as identity keys
        /// Frames where frame number == frameCount are terminator markers and
        /// are not real keys; we skip them too.
        /// </summary>
        private static void ApplyRotationAnimation(NodeBuilder node, int boneIndex, NomFile nom, string trackName)
        {
            if (boneIndex >= nom.rotationFrameList.Count) return;
            var frames = nom.rotationFrameList[boneIndex];
            if (frames == null) return;

            // NOM rotation keys come in several encodings. Earlier we only
            // handled type 0 and wrongly treated 8-0xB as identity resets —
            // that left leg bones (which use the compact single-axis and
            // "hold previous" encodings heavily) completely unanimated.
            //
            // The full type table, confirmed against the format:
            //   0x0  full quaternion: data = (X, Y, Z, W)         — 4 values
            //   0x5  partial quaternion: data = (X, W)            — 2 values
            //   0x6  partial quaternion: data = (Y, W)            — 2 values
            //   0x7  partial quaternion: data = (Z, W)            — 2 values
            //   0x8  hold: reuse the previous frame's quaternion  — 0 values
            //   0x9  hold (variant)                               — 0 values
            //   0xA  hold (variant)                               — 0 values
            //   0xB  hold (variant)                               — 0 values
            //
            // The 0x5/6/7 partials are NOT interpolation hints — they're real
            // keyframes that just happen to store a quaternion with three of
            // its four components zero. The 0x8..0xB "hold" types repeat the
            // last quaternion, which is how the format encodes a bone that
            // stays still for a stretch of frames; they are NOT identity.
            //
            // We carry a running `lastQuat` so the hold types can reference
            // the previous key, exactly as the format intends.

            var keys = new List<(float time, Quaternion value)>();
            Quaternion lastQuat = Quaternion.Identity;
            bool haveLast = false;

            foreach (var nf in frames)
            {
                if (nf.frame >= nom.frameCount) continue;  // terminator
                float t = nf.frame / nom.frameRate;

                Quaternion q;
                bool produced = true;

                switch (nf.type)
                {
                    case 0x0:
                        // Full quaternion (X, Y, Z, W).
                        if (nf.data.Count >= 4)
                            q = new Quaternion(nf.data[0], nf.data[1], nf.data[2], nf.data[3]);
                        else
                        {
                            produced = false;
                            q = Quaternion.Identity;
                        }
                        break;

                    case 0x5:
                        // Partial: only the X component plus W are stored.
                        if (nf.data.Count >= 2)
                            q = new Quaternion(nf.data[0], 0f, 0f, nf.data[1]);
                        else { produced = false; q = Quaternion.Identity; }
                        break;

                    case 0x6:
                        // Partial: only the Y component plus W are stored.
                        if (nf.data.Count >= 2)
                            q = new Quaternion(0f, nf.data[0], 0f, nf.data[1]);
                        else { produced = false; q = Quaternion.Identity; }
                        break;

                    case 0x7:
                        // Partial: only the Z component plus W are stored.
                        if (nf.data.Count >= 2)
                            q = new Quaternion(0f, 0f, nf.data[0], nf.data[1]);
                        else { produced = false; q = Quaternion.Identity; }
                        break;

                    case 0x8:
                    case 0x9:
                    case 0xA:
                    case 0xB:
                        // "Hold" key — reuse the previous quaternion. If this
                        // is somehow the first key in the list (no previous to
                        // hold), fall back to identity.
                        q = haveLast ? lastQuat : Quaternion.Identity;
                        break;

                    default:
                        // Unknown type — skip rather than guess. A wrong key
                        // is worse than a missing one (glTF will SLERP across
                        // the gap).
                        produced = false;
                        q = Quaternion.Identity;
                        break;
                }

                if (!produced) continue;

                // Defensive normalize. The partial-quaternion encodings in
                // particular can be slightly off unit length once decoded, and
                // glTF expects unit quaternions on rotation tracks.
                if (q.LengthSquared() > 0.0f) q = Quaternion.Normalize(q);
                else q = Quaternion.Identity;

                lastQuat = q;
                haveLast = true;
                keys.Add((t, q));
            }

            if (keys.Count == 0) return;

            // Dedupe identical timestamps — glTF requires strictly increasing
            // time values per track. Sort, then keep the last entry at each
            // time (later writes win, matching how the game would replay them).
            keys.Sort((a, b) => a.time.CompareTo(b.time));
            for (int i = keys.Count - 1; i > 0; i--)
            {
                if (Math.Abs(keys[i].time - keys[i - 1].time) < 1e-7f)
                    keys.RemoveAt(i - 1);
            }

            // SharpGLTF's curve API: UseRotation(track) returns a curve builder
            // directly, and each keyframe is added via WithPoint(time, value).
            var curve = node.UseRotation(trackName);
            foreach (var k in keys)
            {
                curve.WithPoint(k.time, k.value);
            }
        }

        /// <summary>
        /// Read X/Y/Z position frames from the NOM and combine them into a
        /// single translation track on the node.
        ///
        /// NomFile stores position frames as three separate per-axis lists.
        /// The per-frame type decoding (including the 0x8/9/A "hold previous"
        /// types) is handled in <see cref="ExtractAxisKeys"/>; see that method
        /// for the full type table.
        ///
        /// glTF wants a single Vector3 track, not three scalar tracks, so we
        /// merge the per-axis keyframes onto a unified timeline. Axes that
        /// don't have a key at a given time inherit their "current" value
        /// (whatever was last seen on that axis).
        /// </summary>
        private static void ApplyTranslationAnimation(NodeBuilder node, int boneIndex, NomFile nom, string trackName)
        {
            // Pull per-axis keys.
            var xKeys = ExtractAxisKeys(boneIndex < nom.xPositionFrameList.Count ? nom.xPositionFrameList[boneIndex] : null, nom);
            var yKeys = ExtractAxisKeys(boneIndex < nom.yPositionFrameList.Count ? nom.yPositionFrameList[boneIndex] : null, nom);
            var zKeys = ExtractAxisKeys(boneIndex < nom.zPositionFrameList.Count ? nom.zPositionFrameList[boneIndex] : null, nom);

            if (xKeys.Count == 0 && yKeys.Count == 0 && zKeys.Count == 0) return;

            // Collect the union of all keyframe times across the three axes.
            // glTF requires one (time, Vector3) sample per keyframe; if X has
            // a key at t=0.1 but Y doesn't, we need to emit a sample anyway
            // and fill Y with its last-known value at that moment.
            var allTimes = new SortedSet<float>();
            foreach (var k in xKeys) allTimes.Add(k.time);
            foreach (var k in yKeys) allTimes.Add(k.time);
            foreach (var k in zKeys) allTimes.Add(k.time);

            // Walk the merged timeline. For each axis, we step through its key
            // list in order, keeping the "current" value updated as we cross
            // its keys. The axis value at any merged time t is whichever of
            // its keys most recently elapsed at or before t.
            var curve = node.UseTranslation(trackName);
            float curX = 0f, curY = 0f, curZ = 0f;
            int ix = 0, iy = 0, iz = 0;

            foreach (var t in allTimes)
            {
                while (ix < xKeys.Count && xKeys[ix].time <= t) { curX = xKeys[ix].value; ix++; }
                while (iy < yKeys.Count && yKeys[iy].time <= t) { curY = yKeys[iy].value; iy++; }
                while (iz < zKeys.Count && zKeys[iz].time <= t) { curZ = zKeys[iz].value; iz++; }
                curve.WithPoint(t, new Vector3(curX, curY, curZ));
            }
        }

        /// <summary>
        /// Pull (time, value) keys out of one of NomFile's per-axis position
        /// frame lists. Returns a sorted, dedupe'd list.
        ///
        /// Position frame types, confirmed against the format:
        ///   0x0  direct value: data[0] is the position on this axis
        ///   0x2  4-value key: data[0] is the position (the other 3 are
        ///        auxiliary data we don't currently use)
        ///   0x4  interpolated key: data[0] is the target position; the
        ///        remaining values are tangents we ignore (linear is close)
        ///   0x8  hold: reuse the PREVIOUS frame's value — NOT zero
        ///   0x9  hold (variant)
        ///   0xA  hold (variant)
        ///
        /// The earlier version of this method treated type 0x8 as a literal
        /// 0.0, which would yank a bone to the origin on that axis instead of
        /// holding its prior value. Combined with the rotation-type bug, that
        /// contributed to leg bones (which lean on the compact encodings)
        /// animating incorrectly or not at all.
        /// </summary>
        private static List<(float time, float value)> ExtractAxisKeys(List<NomFile.NomFrame> frames, NomFile nom)
        {
            var result = new List<(float, float)>();
            if (frames == null) return result;

            // Running "last value" so the hold types (0x8/9/A) can repeat the
            // previous frame's value, matching how the format encodes a bone
            // that stays still on this axis for a stretch.
            float lastValue = 0.0f;
            bool haveLast = false;

            foreach (var nf in frames)
            {
                if (nf.frame >= nom.frameCount) continue;  // terminator
                float t = nf.frame / nom.frameRate;

                float value;
                bool produced = true;

                switch (nf.type)
                {
                    case 0x0:
                        // Direct value.
                        if (nf.data.Count >= 1) value = nf.data[0];
                        else { produced = false; value = 0f; }
                        break;

                    case 0x2:
                        // 4-value key; data[0] is the position component.
                        if (nf.data.Count >= 1) value = nf.data[0];
                        else { produced = false; value = 0f; }
                        break;

                    case 0x4:
                        // Interpolated key — data[0] is the target position.
                        // The remaining values are tangents; glTF could carry
                        // cubic splines but doing so reliably needs the game's
                        // tangent convention, which we haven't reverse-
                        // engineered. Linear interpolation between targets is
                        // visually close.
                        if (nf.data.Count >= 1) value = nf.data[0];
                        else { produced = false; value = 0f; }
                        break;

                    case 0x6:
                        // 3-value key; like 0x2 and 0x4, data[0] is the
                        // position component we use. The other values are
                        // auxiliary data we don't currently interpret.
                        if (nf.data.Count >= 1) value = nf.data[0];
                        else { produced = false; value = 0f; }
                        break;

                    case 0x8:
                    case 0x9:
                    case 0xA:
                        // Hold key — repeat the previous frame's value. If
                        // there's no previous (this is the first key), fall
                        // back to 0; that's degenerate input but we don't
                        // want to throw mid-export.
                        value = haveLast ? lastValue : 0f;
                        break;

                    default:
                        // Unknown type — skip rather than guess.
                        produced = false;
                        value = 0f;
                        break;
                }

                if (!produced) continue;

                lastValue = value;
                haveLast = true;
                result.Add((t, value));
            }

            // Sort by time, dedupe duplicate timestamps (last-write-wins).
            result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            for (int i = result.Count - 1; i > 0; i--)
            {
                if (Math.Abs(result[i].Item1 - result[i - 1].Item1) < 1e-7f)
                    result.RemoveAt(i - 1);
            }
            return result;
        }

        /// <summary>
        /// Compose three per-axis Euler angles (in radians) into a quaternion
        /// using PSU's XZY rotation order: rotate around X first, then Z,
        /// then Y. This is the Sonic Team NN-format convention
        /// (NND_NODETYPE_ROTATE_TYPE_XZY in the original SDK).
        ///
        /// Quaternion multiplication composes right-to-left with respect to
        /// "which rotation is applied first to a vector": the rightmost factor
        /// is applied first. So XZY order — X first — means the quaternion is
        /// qY * qZ * qX.
        ///
        /// This lives in the exporter (not in PSULib's XnjFile) on purpose:
        /// XnjFile stays System.Numerics-free, and the math types are only
        /// pulled in here, where SharpGLTF already provides them.
        /// </summary>
        private static Quaternion EulerXzyToQuaternion(float radX, float radY, float radZ)
        {
            Quaternion qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, radX);
            Quaternion qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radY);
            Quaternion qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radZ);

            // XZY: apply X, then Z, then Y. Right-to-left composition.
            Quaternion q = qy * qz * qx;

            // Normalize defensively — successive multiplications can drift the
            // magnitude slightly, and glTF expects unit quaternions on node
            // transforms.
            if (q.LengthSquared() > 0.0f)
                q = Quaternion.Normalize(q);
            else
                q = Quaternion.Identity;
            return q;
        }
    }
}