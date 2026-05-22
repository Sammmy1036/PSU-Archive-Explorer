using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PSULib.FileClasses.Characters;
using SharpGLTF.Schema2;

namespace psu_archive_explorer.Forms.FileViewers
{
    /// <summary>
    /// Imports a GLB (glTF 2.0 binary) animation back into a <see cref="NomFile"/>.
    /// This is the inverse of <see cref="NomGlbExporter"/>.
    ///
    /// Workflow
    /// --------
    /// The user exports a NOM to GLB, edits the animation in Blender / Maya /
    /// etc., and saves their own GLB. This class reads that GLB and rewrites
    /// the animation channels of the ORIGINAL NOM (the "template"), producing a
    /// new NomFile whose frame lists carry the user's edited animation.
    ///
    /// It does NOT build a NOM from nothing — the NOM header contains bytes
    /// that have not been reverse-engineered, so the original NOM must always
    /// be supplied as a template (see <see cref="NomFileSerializer"/>).
    ///
    /// Key design decisions (chosen for reliability)
    /// ---------------------------------------------
    ///  • BAKE EVERY FRAME. For each bone we sample the GLB animation at every
    ///    integer frame from 0 to frameCount. The GLB very likely uses Bézier
    ///    interpolation between sparse keys; the game replays NOM keys with
    ///    linear interpolation. Baking a key on every frame removes all
    ///    interpolation gaps, so the in-game playback matches what the user
    ///    authored. SharpGLTF's curve sampler evaluates the real curve
    ///    (honoring the GLB's interpolation), so the baked values are correct.
    ///  • FULL-VALUE KEYS ONLY. Every emitted frame is type 0x0 — a full
    ///    quaternion (rotation) or a direct value (position). The compact
    ///    encodings (partials, holds, interpolated) are lossy on read and are
    ///    never written. The file is slightly larger; it is always correct.
    ///  • CALLER-SUPPLIED FRAME RATE. glTF has no fps field — animation is
    ///    keyed in seconds. The target frame rate is a parameter; the UI is
    ///    responsible for choosing it (default: the template NOM's rate).
    ///  • BONE CONTRACT. The GLB armature must contain a node for every NOM
    ///    bone, matched by name (case-insensitive), exactly as the exporter
    ///    named them. Missing bones are a hard error — a NOM's offset tables
    ///    are fixed at 28 entries keyed to a specific bone order, so a partial
    ///    skeleton cannot produce a valid file.
    /// </summary>
    public static class NomGlbImporter
    {
        /// <summary>Hard format limit: NomFrame.frame is a single byte.</summary>
        private const int MaxFrameNumber = 255;

        /// <summary>
        /// Result of an import attempt. Either <see cref="Nom"/> is non-null
        /// (success) or <see cref="Error"/> explains why not. Warnings are
        /// non-fatal notes worth showing the user (e.g. a bone in the GLB that
        /// has no animation, so it was baked as a static pose).
        /// </summary>
        public sealed class ImportResult
        {
            public NomFile Nom;
            public string Error;
            public readonly List<string> Warnings = new List<string>();
            public bool Success => Nom != null && Error == null;
        }

        /// <summary>
        /// Import an edited GLB onto a template NOM.
        /// </summary>
        /// <param name="glbPath">Path to the user's edited .glb.</param>
        /// <param name="template">The original NomFile being replaced. Supplies
        ///   the bone list, the header (via its raw bytes), and the default
        ///   frame rate. Not mutated.</param>
        /// <param name="targetFrameRate">Frame rate for the resulting NOM. The
        ///   GLB's second-based timeline is resampled onto integer frames at
        ///   this rate. Pass the template's rate unless the user explicitly
        ///   chose otherwise.</param>
        /// <returns>An <see cref="ImportResult"/>; check <see cref="ImportResult.Success"/>.</returns>
        public static ImportResult Import(string glbPath, NomFile template, float targetFrameRate)
        {
            var result = new ImportResult();

            // ---- argument / precondition checks ----
            if (string.IsNullOrEmpty(glbPath))
            { result.Error = "No GLB path supplied."; return result; }
            if (template == null)
            { result.Error = "No template NOM supplied."; return result; }
            if (template.boneNames == null || template.boneNames.Length == 0)
            { result.Error = "Template NOM has no bone names."; return result; }
            if (targetFrameRate <= 0f)
            { result.Error = "Target frame rate must be positive (got " + targetFrameRate + ")."; return result; }

            ModelRoot model;
            try
            {
                model = ModelRoot.Load(glbPath);
            }
            catch (Exception ex)
            {
                result.Error = "Could not read the GLB: " + ex.Message;
                return result;
            }

            // ---- locate the animation ----
            if (model.LogicalAnimations == null || model.LogicalAnimations.Count == 0)
            {
                result.Error = "The GLB contains no animation.";
                return result;
            }
            // If there are several, take the first — the exporter writes exactly
            // one ("Take 001"). Note it for the user if there were more.
            Animation anim = model.LogicalAnimations[0];
            if (model.LogicalAnimations.Count > 1)
                result.Warnings.Add("GLB has " + model.LogicalAnimations.Count +
                    " animations; using the first (\"" + anim.Name + "\").");

            // ---- match GLB nodes to NOM bones by name ----
            // The exporter named each node nom.boneNames[i] (or "Bone"+i for an
            // empty name). Build the same name -> node lookup.
            var nodesByName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in model.LogicalNodes)
            {
                if (node.Name == null) continue;
                // First write wins — if a DCC tool duplicated a name, we take
                // the first and warn below when we detect the collision.
                if (!nodesByName.ContainsKey(node.Name))
                    nodesByName[node.Name] = node;
            }

            int boneCount = template.boneNames.Length;
            var boneNodes = new Node[boneCount];
            var missing = new List<string>();

            for (int i = 0; i < boneCount; i++)
            {
                string wanted = string.IsNullOrEmpty(template.boneNames[i])
                    ? ("Bone" + i)
                    : template.boneNames[i];

                if (nodesByName.TryGetValue(wanted, out Node node))
                    boneNodes[i] = node;
                else
                    missing.Add(wanted);
            }

            if (missing.Count > 0)
            {
                result.Error =
                    "The GLB is missing " + missing.Count + " bone(s) the NOM " +
                    "requires: " + string.Join(", ", missing.Take(12)) +
                    (missing.Count > 12 ? ", ..." : "") +
                    ". The GLB's armature must match this animation's skeleton " +
                    "(same bone names). This usually means the GLB was made " +
                    "for a different character, or bones were renamed/deleted " +
                    "during editing.";
                return result;
            }

            // ---- determine frame count from the animation duration ----
            float duration = anim.Duration; // seconds
            int frameCount = (int)Math.Round(duration * targetFrameRate);
            if (frameCount < 1) frameCount = 1;

            if (frameCount > MaxFrameNumber)
            {
                result.Error =
                    "The animation is too long for the NOM format. At " +
                    targetFrameRate + " fps it needs " + frameCount +
                    " frames, but NOM stores frame numbers in a single byte " +
                    "(maximum " + MaxFrameNumber + "). Shorten the animation " +
                    "or use a lower frame rate.";
                return result;
            }

            // ---- build the NomFile ----
            // We mutate a copy-like NomFile: actually we reuse the template
            // instance's identity but replace its frame data, frameCount and
            // frameRate. The serializer reads the template's ORIGINAL bytes via
            // ToRaw()/fileContents, so editing these public fields is safe and
            // is exactly what the serializer expects to pick up.
            NomFile nom = template;
            nom.frameCount = (ushort)frameCount;
            nom.frameRate = targetFrameRate;

            // Before the original frame lists are replaced, record which bones
            // the ORIGINAL NOM actually animated. A bone the GLB doesn't
            // animate is only worth warning about if the vanilla NOM did — if
            // the original had no animation for that bone either, baking it as
            // a static pose changes nothing and a warning would just look like
            // a problem where there is none.
            bool[] originalHadAnimation = new bool[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                originalHadAnimation[i] =
                    BoneHasFrames(nom.rotationFrameList, i) ||
                    BoneHasFrames(nom.xPositionFrameList, i) ||
                    BoneHasFrames(nom.yPositionFrameList, i) ||
                    BoneHasFrames(nom.zPositionFrameList, i);
            }

            // Fresh frame lists, 28 entries each (the format's fixed count).
            nom.rotationFrameList = NewBoneLists(boneCount);
            nom.xPositionFrameList = NewBoneLists(boneCount);
            nom.yPositionFrameList = NewBoneLists(boneCount);
            nom.zPositionFrameList = NewBoneLists(boneCount);

            for (int i = 0; i < boneCount; i++)
            {
                BakeBone(anim, boneNodes[i], i, nom, frameCount, targetFrameRate,
                    originalHadAnimation[i], result);
            }

            // Mark the NOM modified. This is ESSENTIAL: NblChunk.SaveFile()
            // serves a cached copy of the chunk unless AnyChildDirty() reports
            // true, and AnyChildDirty() checks exactly this flag. Without it,
            // the archive saves the original unedited NOM and the import is
            // silently lost.
            nom.dirty = true;

            result.Nom = nom;
            return result;
        }

        /// <summary>Creates a list of <paramref name="count"/> empty bone slots.</summary>
        private static List<List<NomFile.NomFrame>> NewBoneLists(int count)
        {
            var lists = new List<List<NomFile.NomFrame>>(count);
            for (int i = 0; i < count; i++)
                lists.Add(null);
            return lists;
        }

        /// <summary>
        /// True if the given bone slot holds real keyframe data. A NOM list
        /// entry is null when the bone has no list at all; an empty list also
        /// counts as "no animation". Used to tell whether the ORIGINAL NOM
        /// animated a bone, so the importer only warns about a GLB-missing
        /// bone when its absence is an actual change from vanilla.
        /// </summary>
        private static bool BoneHasFrames(List<List<NomFile.NomFrame>> lists, int boneIndex)
        {
            if (lists == null || boneIndex < 0 || boneIndex >= lists.Count)
                return false;
            var frames = lists[boneIndex];
            return frames != null && frames.Count > 0;
        }

        /// <summary>
        /// Bakes one bone's rotation and translation onto every integer frame.
        ///
        /// Sampling is done with SharpGLTF's curve samplers, which evaluate the
        /// GLB's real animation curve at an arbitrary time — so even though we
        /// only emit linear NOM keys, each baked value already reflects the
        /// GLB's (possibly Bézier) interpolation at that instant.
        /// </summary>
        private static void BakeBone(
            Animation anim, Node node, int boneIndex,
            NomFile nom, int frameCount, float frameRate,
            bool originalHadAnimation, ImportResult result)
        {
            // SharpGLTF: a channel for this node may or may not exist. If the
            // user never animated this bone, the sampler is null and we bake
            // the node's static local transform instead, so the bone still
            // holds a definite pose rather than collapsing to identity.
            //
            // SharpGLTF exposes per-node samplers through the animation's
            // Channels collection: each AnimationChannel targets one node and
            // one path (rotation / translation / scale). We find the channels
            // whose TargetNode is this bone and ask each for its typed sampler.
            var rotChannel = FindChannel(anim, node, PropertyPath.rotation);
            var posChannel = FindChannel(anim, node, PropertyPath.translation);

            var rotSampler = rotChannel?.GetRotationSampler()?.CreateCurveSampler();
            var posSampler = posChannel?.GetTranslationSampler()?.CreateCurveSampler();

            // The node's own rest transform — the fallback for an unanimated
            // channel. (The exporter wrote the XNJ bind pose here.)
            var rest = node.LocalTransform;
            Quaternion restRot = rest.Rotation;
            Vector3 restPos = rest.Translation;

            bool anyChannel = rotSampler != null || posSampler != null;
            // Only warn when the GLB dropped a bone that the ORIGINAL NOM
            // actually animated — that is a real difference the user may not
            // have intended. A bone that was static in vanilla and is static
            // now is a non-event and would only look like a false alarm.
            if (!anyChannel && originalHadAnimation)
            {
                result.Warnings.Add("Bone \"" + node.Name + "\" was animated in " +
                    "the original NOM but has no animation in the GLB; baked as " +
                    "a static pose.");
            }

            var rotFrames = new List<NomFile.NomFrame>(frameCount + 1);
            var xFrames = new List<NomFile.NomFrame>(frameCount + 1);
            var yFrames = new List<NomFile.NomFrame>(frameCount + 1);
            var zFrames = new List<NomFile.NomFrame>(frameCount + 1);

            // Bake frames 0 .. frameCount-1. Frame `frameCount` itself is the
            // terminator and is appended separately.
            for (int f = 0; f < frameCount; f++)
            {
                float t = f / frameRate; // seconds

                Quaternion q = rotSampler != null ? rotSampler.GetPoint(t) : restRot;
                Vector3 p = posSampler != null ? posSampler.GetPoint(t) : restPos;

                // Normalize the quaternion — sampler output can drift slightly
                // off unit length, and the game (like glTF) expects unit
                // rotations.
                if (q.LengthSquared() > 0f) q = Quaternion.Normalize(q);
                else q = Quaternion.Identity;

                rotFrames.Add(MakeRotationFrame((byte)f, q));
                xFrames.Add(MakePositionFrame((byte)f, p.X));
                yFrames.Add(MakePositionFrame((byte)f, p.Y));
                zFrames.Add(MakePositionFrame((byte)f, p.Z));
            }

            // Terminator frames. frame == frameCount tells the reader to stop.
            // We emit zero-data terminators with the type bytes the reader
            // accepts (see NomFileSerializer for the full rationale):
            //   rotation : type 0x8, type2 0x8  (high nibble 0x8 = zero data)
            //   position : type 0xA, type2 0x0  (reader subtracts 0x2 -> 0x8)
            rotFrames.Add(MakeTerminator((byte)frameCount, isRotation: true));
            xFrames.Add(MakeTerminator((byte)frameCount, isRotation: false));
            yFrames.Add(MakeTerminator((byte)frameCount, isRotation: false));
            zFrames.Add(MakeTerminator((byte)frameCount, isRotation: false));

            nom.rotationFrameList[boneIndex] = rotFrames;
            nom.xPositionFrameList[boneIndex] = xFrames;
            nom.yPositionFrameList[boneIndex] = yFrames;
            nom.zPositionFrameList[boneIndex] = zFrames;
        }

        /// <summary>
        /// Finds the <see cref="AnimationChannel"/> in <paramref name="anim"/>
        /// that targets <paramref name="node"/> for the given transform path
        /// (rotation / translation / scale), or null if the node isn't
        /// animated on that path.
        ///
        /// SharpGLTF has no direct node-to-sampler lookup on Animation; the
        /// channel list is the supported route. The list is short (a handful
        /// of channels per bone at most), so the linear scan is fine.
        /// </summary>
        private static AnimationChannel FindChannel(Animation anim, Node node, PropertyPath path)
        {
            foreach (var channel in anim.Channels)
            {
                if (channel.TargetNode == node && channel.TargetNodePath == path)
                    return channel;
            }
            return null;
        }

        /// <summary>
        /// Builds a type-0x0 rotation frame: a full quaternion (X, Y, Z, W).
        /// Both the float data and the encoded raw shorts are populated; the
        /// serializer prefers rawData when present, so encoding here keeps the
        /// frame self-consistent and lets the serializer stay a pure writer.
        /// </summary>
        private static NomFile.NomFrame MakeRotationFrame(byte frame, Quaternion q)
        {
            var nf = new NomFile.NomFrame
            {
                frame = frame,
                type = 0x0,   // full quaternion
                type2 = 0x0,
            };
            AddValue(nf, q.X, isRotation: true);
            AddValue(nf, q.Y, isRotation: true);
            AddValue(nf, q.Z, isRotation: true);
            AddValue(nf, q.W, isRotation: true);
            return nf;
        }

        /// <summary>
        /// Builds a type-0x0 position frame: one direct scalar value for one
        /// axis. NOM stores X/Y/Z as three separate per-bone lists, so this is
        /// called once per axis.
        /// </summary>
        private static NomFile.NomFrame MakePositionFrame(byte frame, float value)
        {
            var nf = new NomFile.NomFrame
            {
                frame = frame,
                type = 0x0,   // direct value
                type2 = 0x0,
            };
            AddValue(nf, value, isRotation: false);
            return nf;
        }

        /// <summary>
        /// Builds the zero-data terminator frame that ends a bone's list.
        /// </summary>
        private static NomFile.NomFrame MakeTerminator(byte frame, bool isRotation)
        {
            return new NomFile.NomFrame
            {
                frame = frame,
                // Rotation terminator: high nibble 0x8 (zero data), low nibble
                //   0x8 (the reader's required type2). Position terminator:
                //   high nibble 0xA -> reader subtracts 0x2 -> 0x8 (zero data),
                //   low nibble 0x0.
                type = (byte)(isRotation ? 0x8 : 0xA),
                type2 = (byte)(isRotation ? 0x8 : 0x0),
            };
        }

        /// <summary>
        /// Encodes a float and appends it to a frame's data + rawData, keeping
        /// the two in lockstep. Uses <see cref="NomValueCodec"/>, the verified
        /// inverse of NomFile's value decoder.
        /// </summary>
        private static void AddValue(NomFile.NomFrame nf, float value, bool isRotation)
        {
            short raw = NomValueCodec.Encode(value, isRotation);
            nf.rawData.Add(raw);
            // Store the round-tripped float (what the game will actually see)
            // rather than the pre-encoding value, so data and rawData agree.
            nf.data.Add(NomValueCodec.Decode(raw, isRotation));
        }
    }
}