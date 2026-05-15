using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Windows.Forms;
using PSULib.FileClasses.Characters;
using PSULib.FileClasses.Models;

namespace psu_archive_explorer
{
    /// <summary>
    /// In-app animation preview for NOM files, rendered with GDI+ (System.Drawing)
    /// instead of OpenGL.
    ///
    /// WHY GDI+ AND NOT OPENGL: the OpenGL version depended on a SharpGL render
    /// context being initialized at the right point in the control lifecycle,
    /// which is fragile to get right when the control is created by hand rather
    /// than through the Designer. A double-buffered Panel with a Paint handler
    /// has none of that ceremony — there is no context to create, no driver
    /// path, no library-version sensitivity. The trade-off is a flat 2D
    /// projection rather than a free-orbiting 3D camera, which is fine for a
    /// "check the motion before exporting" preview.
    ///
    /// WHAT IS REUSED: every bit of the actual animation math is renderer-
    /// independent — BuildSkeleton, UpdateBoneTransforms, SampleRotation,
    /// SamplePosition, SampleAxis, EulerXzyToQuaternion. They produce a list of
    /// world-space bone matrices. The ONLY thing that differs from the OpenGL
    /// version is the final step: projecting those matrices to 2D and drawing
    /// lines/dots, versus issuing OpenGL vertex calls.
    ///
    /// PUBLIC SURFACE: deliberately identical to the OpenGL version
    /// (LoadAnimation, IsPlaying, CurrentTime, Play/Pause/TogglePlayPause,
    /// Restart, StartPlayback, ResetAnimation, InvalidatePreview) so the
    /// hosting NomFileViewer needs NO changes — it already calls exactly these.
    /// </summary>
    public partial class NomAnimationPreview : UserControl
    {
        // ---- rendering surface ----
        private Panel renderPanel;
        private Button viewToggleButton;

        // ---- animation source ----
        private NomFile nom;
        private XnjFile skeleton;

        // ---- playback state ----
        private Timer timer;
        private bool isPlaying = false;
        private float currentTime = 0f;
        private float playbackSpeed = 1.0f;
        private DateTime lastFrameTime = DateTime.Now;

        // ---- skeleton data ----
        //
        // We store the rest pose as separate TRS *components* (not a single
        // baked matrix), exactly like NomGlbExporter does — it sets each
        // SharpGLTF node's LocalTransform to AffineTransform(scale, rotation,
        // translation). That matters because glTF animation channels REPLACE
        // a node's rotation/translation; they don't multiply on top of the
        // rest pose. To match what Blender shows, the preview has to layer
        // animation the same way: sampled value if the bone has keys on that
        // channel, otherwise the rest-pose value.
        private readonly List<Vector3> restTranslation = new List<Vector3>();
        private readonly List<Quaternion> restRotation = new List<Quaternion>();
        private readonly List<Vector3> restScale = new List<Vector3>();
        private readonly List<int> parentIndices = new List<int>();

        // Whether each bone actually has any keys on the rotation / position
        // channels. Mirrors the exporter's "only emit a curve if there's
        // data" behaviour: a bone with no rotation keys keeps its rest-pose
        // rotation for the whole take rather than snapping to identity.
        private readonly List<bool> hasRotationKeys = new List<bool>();
        private readonly List<bool> hasPositionKeys = new List<bool>();

        // Computed each frame: final world-space matrix per bone.
        private readonly List<Matrix4x4> worldMatrices = new List<Matrix4x4>();

        // ---- projection ----
        /// <summary>
        /// Which 2D projection of the 3D skeleton to draw. The preview is a
        /// flat orthographic projection (no free camera), so each mode just
        /// picks which two world axes map to screen X and screen Y — see
        /// Flatten() for the per-mode mapping.
        ///   Front  — looking along +Z: see the character's front
        ///   Back   — looking along -Z: see the character's back
        ///   Side1  — looking along the character's left/right (one side)
        ///   Side2  — the opposite side
        ///   Top    — looking straight down: see the character from above
        ///   Bottom — looking straight up: see the character from below
        /// </summary>
        private enum ViewMode { Front, Back, Side1, Side2, Top, Bottom }
        private ViewMode viewMode = ViewMode.Front;

        /// <summary>
        /// The view cycle order and their button labels, kept together so the
        /// toggle handler stays a simple "advance to next" with no switch.
        /// </summary>
        private static readonly ViewMode[] viewCycle =
        {
            ViewMode.Front, ViewMode.Back, ViewMode.Side1,
            ViewMode.Side2, ViewMode.Top, ViewMode.Bottom
        };

        private static string ViewModeLabel(ViewMode m)
        {
            switch (m)
            {
                case ViewMode.Front: return "Front";
                case ViewMode.Back: return "Back";
                case ViewMode.Side1: return "Side 1";
                case ViewMode.Side2: return "Side 2";
                case ViewMode.Top: return "Top";
                case ViewMode.Bottom: return "Bottom";
                default: return m.ToString();
            }
        }

        public NomAnimationPreview()
        {
            InitializeComponent();
            SetupTimer();
        }

        /// <summary>
        /// Builds the control's child controls by hand. Unlike the OpenGL
        /// version this needs no ISupportInitialize bracketing — a Panel is
        /// just a Panel. We enable double buffering on the render panel via a
        /// tiny subclass so playback doesn't flicker.
        /// </summary>
        private void InitializeComponent()
        {
            this.renderPanel = new DoubleBufferedPanel();
            this.renderPanel.Dock = DockStyle.Fill;
            this.renderPanel.BackColor = Color.FromArgb(20, 20, 30);
            this.renderPanel.Paint += RenderPanel_Paint;
            this.renderPanel.Resize += (s, e) => this.renderPanel.Invalidate();

            // Small overlay button to cycle through the six fixed views
            // (Front, Back, Side 1, Side 2, Top, Bottom). Parented to the
            // render panel so it floats over the drawing.
            this.viewToggleButton = new Button
            {
                Text = "View: Front",
                Width = 110,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 60),
                ForeColor = Color.White,
                Location = new Point(8, 8),
                TabStop = false
            };
            this.viewToggleButton.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 110);
            this.viewToggleButton.Click += ViewToggleButton_Click;

            this.renderPanel.Controls.Add(this.viewToggleButton);
            this.Controls.Add(this.renderPanel);
        }

        private void SetupTimer()
        {
            timer = new Timer { Interval = 16 }; // ~60 Hz
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        // ====================== PUBLIC INTERFACE ======================
        // (identical to the OpenGL version so NomFileViewer is unaffected)

        public bool IsPlaying
        {
            get { return isPlaying; }
            set { isPlaying = value; }
        }

        public float CurrentTime
        {
            get { return currentTime; }
            set
            {
                currentTime = value;
                // Re-pose immediately so a timeline scrub updates the drawing
                // even while playback is paused.
                UpdateBoneTransforms();
                renderPanel?.Invalidate();
                RaiseTimeChanged();
            }
        }

        /// <summary>
        /// Total length of the loaded animation in seconds, or 0 if nothing is
        /// loaded (or the NOM has a zero frame rate). Exposed so the host can
        /// position its timeline slider and time readout without having to
        /// reach into the NomFile itself.
        /// </summary>
        public float Duration
        {
            get
            {
                if (nom == null || nom.frameRate <= 0f) return 0f;
                return nom.frameCount / nom.frameRate;
            }
        }

        /// <summary>
        /// Raised whenever currentTime changes — on every playback tick, on a
        /// timeline scrub, and on Restart / Reset / load. The host viewer
        /// subscribes to this to keep its time label and timeline slider in
        /// sync with the animation; without it those controls would just sit
        /// at 0:00 because nothing else tells them playback advanced.
        /// </summary>
        public event EventHandler TimeChanged;

        private void RaiseTimeChanged()
        {
            TimeChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Playback speed multiplier. Timer_Tick scales its per-tick delta by
        /// this, so 2.0 plays twice as fast, 0.5 half speed, etc. Clamped to a
        /// sane positive range — a zero or negative multiplier would freeze or
        /// reverse playback in ways the UI doesn't expect. Default 1.0.
        /// </summary>
        public float PlaybackSpeed
        {
            get { return playbackSpeed; }
            set
            {
                if (value < 0.05f) value = 0.05f;
                if (value > 8.0f) value = 8.0f;
                playbackSpeed = value;
            }
        }

        public void InvalidatePreview()
        {
            renderPanel?.Invalidate();
        }

        public void LoadAnimation(NomFile nomFile, XnjFile skeletonFile = null)
        {
            nom = nomFile;
            skeleton = skeletonFile ?? VanillaPsuSkeleton.Create();

            BuildSkeleton();
            ResetAnimation();
            UpdateBoneTransforms();   // pose frame 0 right away so the skeleton
                                      // is visible immediately, even paused

            // NOTE: deliberately does NOT auto-play. The host NomFileViewer
            // creates its play button labelled "Play", i.e. it expects the
            // preview to start paused — the user presses Play to begin. The
            // old OpenGL version auto-played, which left the button text and
            // the actual state disagreeing. Starting paused keeps them in sync.
            isPlaying = false;

            renderPanel?.Invalidate();
        }

        public void StartPlayback()
        {
            isPlaying = true;
            lastFrameTime = DateTime.Now;
        }

        public void Play() { isPlaying = true; lastFrameTime = DateTime.Now; }
        public void Pause() { isPlaying = false; }
        public void TogglePlayPause()
        {
            isPlaying = !isPlaying;
            if (isPlaying) lastFrameTime = DateTime.Now;
        }

        public void Restart()
        {
            currentTime = 0;
            lastFrameTime = DateTime.Now;
            UpdateBoneTransforms();
            renderPanel?.Invalidate();
            RaiseTimeChanged();   // snap the host's label / slider back to 0
        }

        public void ResetAnimation()
        {
            currentTime = 0;
            lastFrameTime = DateTime.Now;
            RaiseTimeChanged();
        }

        // ====================== SKELETON SETUP ======================

        /// <summary>
        /// Builds the per-bone rest-pose components and parent table from the
        /// NOM's bone list and the supplied (or vanilla) skeleton.
        ///
        /// This mirrors NomGlbExporter.Export's first pass: it stores the
        /// rest pose as TRS *components* (scale, rotation, translation), the
        /// same data the exporter feeds into AffineTransform per node. It also
        /// records, per bone, whether the NOM actually carries rotation /
        /// position keys — so UpdateBoneTransforms can do exactly what glTF
        /// does: use the animated value where there's a key, fall back to the
        /// rest-pose value where there isn't.
        /// </summary>
        private void BuildSkeleton()
        {
            restTranslation.Clear();
            restRotation.Clear();
            restScale.Clear();
            parentIndices.Clear();
            hasRotationKeys.Clear();
            hasPositionKeys.Clear();
            worldMatrices.Clear();

            if (nom == null || nom.boneNames == null) return;

            int count = nom.boneNames.Length;
            for (int i = 0; i < count; i++)
            {
                int parentIdx = -1;
                Vector3 trans = Vector3.Zero;
                Quaternion rot = Quaternion.Identity;
                Vector3 scale = Vector3.One;

                if (skeleton != null && skeleton.Bones != null && i < skeleton.Bones.Count)
                {
                    var bone = skeleton.Bones[i];
                    parentIdx = bone.ParentIndex;

                    trans = new Vector3(bone.LocalTranslationX,
                                        bone.LocalTranslationY,
                                        bone.LocalTranslationZ);
                    scale = new Vector3(bone.LocalScaleX,
                                        bone.LocalScaleY,
                                        bone.LocalScaleZ);
                    // Same conversion the exporter uses (EulerXzyToQuaternion).
                    rot = EulerXzyToQuaternion(bone.LocalRotationRadX,
                                               bone.LocalRotationRadY,
                                               bone.LocalRotationRadZ);
                }

                parentIndices.Add(parentIdx);
                restTranslation.Add(trans);
                restRotation.Add(rot);
                restScale.Add(scale);
                worldMatrices.Add(Matrix4x4.Identity);

                // Does this bone have any usable rotation keys? The exporter
                // only emits a rotation curve when there's at least one real
                // key; otherwise the node keeps its rest-pose rotation. We
                // detect the same condition here.
                hasRotationKeys.Add(BoneHasRotationKeys(i));
                hasPositionKeys.Add(BoneHasPositionKeys(i));
            }
        }

        /// <summary>True if bone i has at least one rotation key the exporter
        /// would emit (types 0x0, 0x5-0x7, 0x8-0xB), excluding terminators.</summary>
        private bool BoneHasRotationKeys(int boneIndex)
        {
            if (boneIndex >= nom.rotationFrameList.Count) return false;
            var frames = nom.rotationFrameList[boneIndex];
            if (frames == null) return false;
            foreach (var nf in frames)
            {
                if (nf.frame >= nom.frameCount) continue;
                if (nf.type == 0x0 && nf.data.Count >= 4) return true;
                if (nf.type >= 0x5 && nf.type <= 0x7 && nf.data.Count >= 2) return true;
                if (nf.type >= 0x8 && nf.type <= 0xB) return true;
            }
            return false;
        }

        /// <summary>True if bone i has at least one position key on any axis.</summary>
        private bool BoneHasPositionKeys(int boneIndex)
        {
            return AxisHasKeys(nom.xPositionFrameList, boneIndex)
                || AxisHasKeys(nom.yPositionFrameList, boneIndex)
                || AxisHasKeys(nom.zPositionFrameList, boneIndex);
        }

        private bool AxisHasKeys(List<List<NomFile.NomFrame>> lists, int boneIndex)
        {
            if (boneIndex >= lists.Count) return false;
            var frames = lists[boneIndex];
            if (frames == null) return false;
            foreach (var nf in frames)
            {
                if (nf.frame >= nom.frameCount) continue;
                if (nf.data.Count >= 1 || (nf.type >= 0x8 && nf.type <= 0xA)) return true;
            }
            return false;
        }

        // ====================== PLAYBACK LOOP ======================

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!isPlaying || nom == null) return;

            float delta = (float)(DateTime.Now - lastFrameTime).TotalSeconds * playbackSpeed;
            lastFrameTime = DateTime.Now;

            currentTime += delta;
            float duration = nom.frameRate > 0 ? nom.frameCount / nom.frameRate : 0f;
            if (duration > 0f && currentTime > duration)
                currentTime %= duration;

            UpdateBoneTransforms();
            renderPanel?.Invalidate();
            RaiseTimeChanged();   // keep the host's time label / slider in sync
        }

        /// <summary>
        /// Recomputes every bone's world matrix for the current time.
        ///
        /// This is the method that was wrong before. The key insight from
        /// NomGlbExporter: glTF animation channels REPLACE a node's local
        /// rotation/translation — they are not multiplied on top of the rest
        /// pose. The old code did `animLocal * restPose`, double-applying the
        /// rest transform and flinging the joints apart.
        ///
        /// The correct model, matching the exporter + how Blender reads the
        /// GLB:
        ///   localRotation    = sampled rotation   if the bone has rot keys,
        ///                      else the rest-pose rotation
        ///   localTranslation = sampled translation if the bone has pos keys,
        ///                      else the rest-pose translation
        ///   localScale       = rest-pose scale (NOM carries no scale tracks)
        ///   localMatrix      = Scale * Rotation * Translation   (TRS order,
        ///                      same as SharpGLTF's AffineTransform)
        ///   worldMatrix      = localMatrix * parentWorldMatrix
        ///
        /// In System.Numerics' row-vector convention, `A * B` applies A then
        /// B, so `Scale * Rotation * Translation` scales first, then rotates,
        /// then translates — the standard TRS — and `local * parent` puts the
        /// child in the parent's space. Both orders now match the exporter.
        /// </summary>
        private void UpdateBoneTransforms()
        {
            if (nom == null) return;

            for (int i = 0; i < worldMatrices.Count; i++)
            {
                // Rotation: sampled if this bone is animated on rotation,
                // otherwise the rest-pose rotation (NOT identity — that was
                // part of the old breakage).
                Quaternion rot = hasRotationKeys[i]
                    ? SampleRotation(i, currentTime)
                    : restRotation[i];

                // Translation: same rule.
                Vector3 pos = hasPositionKeys[i]
                    ? SamplePosition(i, currentTime)
                    : restTranslation[i];

                // Scale: NOM files don't carry scale animation, so the bone
                // keeps its rest-pose scale for the whole take.
                Vector3 scale = restScale[i];

                // Compose local TRS. Scale * Rotation * Translation in
                // row-vector convention == scale, then rotate, then translate.
                Matrix4x4 local =
                    Matrix4x4.CreateScale(scale)
                    * Matrix4x4.CreateFromQuaternion(rot)
                    * Matrix4x4.CreateTranslation(pos);

                int parent = parentIndices[i];
                if (parent >= 0 && parent < worldMatrices.Count && parent != i)
                    worldMatrices[i] = local * worldMatrices[parent];
                else
                    worldMatrices[i] = local;
            }
        }

        // ---- channel sampling (carried over unchanged) ----

        /// <summary>
        /// Samples a bone's rotation at the given time. The frame-type
        /// handling deliberately matches NomGlbExporter.ApplyRotationAnimation
        /// exactly, so the preview and the GLB export agree:
        ///   0x0       full quaternion (X, Y, Z, W)
        ///   0x5/6/7   partial quaternion (single axis + W)
        ///   0x8-0xB   "hold" — repeat the previous quaternion
        /// "Sample at time t" means: take the most recent key at or before t
        /// (step interpolation). The exporter hands glTF the keys and lets it
        /// SLERP between them; stepping to the last key is a close visual
        /// match for a preview and keeps this simple.
        /// </summary>
        private Quaternion SampleRotation(int boneIndex, float time)
        {
            var frames = boneIndex < nom.rotationFrameList.Count
                ? nom.rotationFrameList[boneIndex] : null;
            if (frames == null || frames.Count == 0) return Quaternion.Identity;

            float targetFrame = time * nom.frameRate;
            Quaternion lastQ = Quaternion.Identity;
            bool haveLast = false;

            foreach (var f in frames)
            {
                if (f.frame > targetFrame) break;
                if (f.frame >= nom.frameCount) continue;

                switch (f.type)
                {
                    case 0x0:
                        if (f.data.Count >= 4)
                        {
                            lastQ = new Quaternion(f.data[0], f.data[1], f.data[2], f.data[3]);
                            haveLast = true;
                        }
                        break;
                    case 0x5:
                        if (f.data.Count >= 2)
                        {
                            lastQ = new Quaternion(f.data[0], 0, 0, f.data[1]);
                            haveLast = true;
                        }
                        break;
                    case 0x6:
                        if (f.data.Count >= 2)
                        {
                            lastQ = new Quaternion(0, f.data[0], 0, f.data[1]);
                            haveLast = true;
                        }
                        break;
                    case 0x7:
                        if (f.data.Count >= 2)
                        {
                            lastQ = new Quaternion(0, 0, f.data[0], f.data[1]);
                            haveLast = true;
                        }
                        break;
                    case 0x8:
                    case 0x9:
                    case 0xA:
                    case 0xB:
                        // "Hold" — reuse the previous quaternion. lastQ
                        // already carries it; nothing to change. (If this is
                        // somehow the first key, lastQ stays identity.)
                        haveLast = true;
                        break;
                }
            }

            if (!haveLast) return Quaternion.Identity;

            float lenSq = lastQ.X * lastQ.X + lastQ.Y * lastQ.Y
                        + lastQ.Z * lastQ.Z + lastQ.W * lastQ.W;
            return lenSq > 0.0001f ? Quaternion.Normalize(lastQ) : Quaternion.Identity;
        }

        private Vector3 SamplePosition(int boneIndex, float time)
        {
            float x = SampleAxis(nom.xPositionFrameList, boneIndex, time);
            float y = SampleAxis(nom.yPositionFrameList, boneIndex, time);
            float z = SampleAxis(nom.zPositionFrameList, boneIndex, time);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Samples one position axis at the given time. Frame-type handling
        /// matches NomGlbExporter.ExtractAxisKeys exactly:
        ///   0x0/0x2/0x4/0x6  use data[0] as the value on this axis
        ///   0x8/0x9/0xA      "hold" — repeat the previous value
        /// Stepping to the most recent key at or before t, same as
        /// SampleRotation.
        /// </summary>
        private float SampleAxis(List<List<NomFile.NomFrame>> lists, int boneIndex, float time)
        {
            if (boneIndex >= lists.Count) return 0f;
            var frames = lists[boneIndex];
            if (frames == null || frames.Count == 0) return 0f;

            float targetFrame = time * nom.frameRate;
            float lastVal = 0f;

            foreach (var f in frames)
            {
                if (f.frame > targetFrame) break;
                if (f.frame >= nom.frameCount) continue;

                switch (f.type)
                {
                    case 0x0:
                    case 0x2:
                    case 0x4:
                    case 0x6:
                        // All of these carry the axis value in data[0]; the
                        // exporter treats them identically and so do we.
                        if (f.data.Count >= 1) lastVal = f.data[0];
                        break;
                    case 0x8:
                    case 0x9:
                    case 0xA:
                        // "Hold" — keep the previous value. lastVal already
                        // holds it; nothing to do.
                        break;
                        // Unknown types: skip, matching the exporter's default.
                }
            }
            return lastVal;
        }

        private static Quaternion EulerXzyToQuaternion(float radX, float radY, float radZ)
        {
            Quaternion qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, radX);
            Quaternion qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, radY);
            Quaternion qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radZ);
            Quaternion q = qy * qz * qx;
            return q.LengthSquared() > 0.0001f ? Quaternion.Normalize(q) : Quaternion.Identity;
        }

        // ====================== RENDERING (GDI+) ======================

        private void ViewToggleButton_Click(object sender, EventArgs e)
        {
            // Advance to the next view in the cycle, wrapping at the end.
            int idx = Array.IndexOf(viewCycle, viewMode);
            idx = (idx + 1) % viewCycle.Length;
            viewMode = viewCycle[idx];
            viewToggleButton.Text = "View: " + ViewModeLabel(viewMode);
            renderPanel.Invalidate();
        }

        /// <summary>
        /// Extracts a bone's world-space position (the translation row of its
        /// world matrix).
        /// </summary>
        private Vector3 GetWorldPosition(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= worldMatrices.Count)
                return Vector3.Zero;
            var m = worldMatrices[boneIndex];
            return new Vector3(m.M41, m.M42, m.M43);
        }

        /// <summary>
        /// Flattens a 3D world position to the chosen 2D plane. The preview is
        /// a fixed orthographic projection — each mode just selects which two
        /// world axes become screen X and screen Y. The caller (RenderPanel_Paint)
        /// then auto-fits whatever box these points span, and inverts screen Y
        /// so larger values draw higher up.
        ///
        /// World axes (PSU convention): X = left/right, Y = up, Z = depth.
        ///
        ///   Front  (-X,  Y) — looking along +Z at the character's front.
        ///                     X is negated: PSU's world X runs opposite to
        ///                     GDI+ screen X for how the character faces, so a
        ///                     straight X mapping shows the BACK mirrored.
        ///                     Negating flips it to a correct front view.
        ///   Back   ( X,  Y) — the opposite look; un-negated X is literally
        ///                     the back, with correct (un-mirrored) handedness.
        ///   Side 1 ( Z,  Y) — looking along the X axis from one side.
        ///   Side 2 (-Z,  Y) — looking from the opposite side (Z negated so
        ///                     it's a true mirror of Side 1, not the same image).
        ///   Top    (-X, -Z) — looking straight down the Y axis. Horizontal
        ///                     stays -X to agree with the Front view; the
        ///                     vertical screen axis is depth (Z). -Z is chosen
        ///                     so the character's front points "down" on screen,
        ///                     i.e. toward the viewer as you look down.
        ///   Bottom (-X,  Z) — looking straight up the Y axis; the vertical
        ///                     axis is flipped relative to Top so it reads as a
        ///                     true mirror.
        ///
        /// Note these return values feed an auto-fit + Y-invert step in
        /// RenderPanel_Paint, so "Y" here means "the axis that should point up
        /// on screen" — that's why Top/Bottom put a Z term in the second slot.
        /// </summary>
        private PointF Flatten(Vector3 worldPos)
        {
            switch (viewMode)
            {
                case ViewMode.Front:
                    return new PointF(-worldPos.X, worldPos.Y);
                case ViewMode.Back:
                    return new PointF(worldPos.X, worldPos.Y);
                case ViewMode.Side1:
                    return new PointF(worldPos.Z, worldPos.Y);
                case ViewMode.Side2:
                    return new PointF(-worldPos.Z, worldPos.Y);
                case ViewMode.Top:
                    return new PointF(-worldPos.X, -worldPos.Z);
                case ViewMode.Bottom:
                    return new PointF(-worldPos.X, worldPos.Z);
                default:
                    return new PointF(-worldPos.X, worldPos.Y);
            }
        }

        /// <summary>
        /// The whole 2D draw. Computes a bounding box over every joint's
        /// flattened position, fits that box into the panel with a margin,
        /// then draws bones as lines and joints as dots. Auto-fit means the
        /// skeleton is always framed sensibly regardless of PSU's unit scale
        /// or where the animation moves the character.
        /// </summary>
        private void RenderPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = renderPanel.ClientSize.Width;
            int h = renderPanel.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            // Nothing loaded yet — show a hint instead of an empty void.
            if (nom == null || worldMatrices.Count == 0)
            {
                DrawCenteredHint(g, w, h, "No animation loaded.");
                return;
            }

            // ---- pass 1: flatten every joint and find the bounding box ----
            var flat = new PointF[worldMatrices.Count];
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < worldMatrices.Count; i++)
            {
                PointF p = Flatten(GetWorldPosition(i));
                flat[i] = p;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            float spanX = maxX - minX;
            float spanY = maxY - minY;
            // Guard against a degenerate (zero-size) skeleton — e.g. a single
            // bone, or everything collapsed to the origin. Without this the
            // scale would be infinite.
            if (spanX < 1e-4f) spanX = 1e-4f;
            if (spanY < 1e-4f) spanY = 1e-4f;

            const float margin = 40f;
            float scale = Math.Min((w - 2 * margin) / spanX,
                                   (h - 2 * margin) / spanY);
            if (scale <= 0f || float.IsInfinity(scale)) scale = 1f;

            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;

            // ---- pass 2: project flattened coords to screen pixels ----
            // Screen Y is inverted (GDI+ origin is top-left, +Y goes down)
            // so the character is drawn right-side up: larger world-Y (up on
            // the character) maps to a SMALLER screen-Y (higher on screen).
            var screen = new PointF[flat.Length];
            for (int i = 0; i < flat.Length; i++)
            {
                float sx = w * 0.5f + (flat[i].X - centerX) * scale;
                float sy = h * 0.5f - (flat[i].Y - centerY) * scale;
                screen[i] = new PointF(sx, sy);
            }

            // ---- pass 3: draw bones (parent -> child lines) ----
            // We skip the line into any bone whose parent is itself a root
            // (the parent's own ParentIndex < 0). In the PSU vanilla skeleton
            // the Root bone sits at the world origin (0,0,0) while the body
            // floats above it, so a naive parent->child line from Root up to
            // Navel renders as a long stray "bone" tethering the figure to
            // the ground. That line isn't real skeleton structure — it's just
            // the root-to-first-bone connector — so suppressing it cleans up
            // the preview without losing anything meaningful.
            using (var bonePen = new Pen(Color.FromArgb(120, 230, 120), 2.5f))
            {
                for (int i = 0; i < screen.Length; i++)
                {
                    int p = parentIndices[i];
                    if (p < 0 || p >= screen.Length) continue;        // i is a root — no line
                    if (parentIndices[p] < 0) continue;               // parent is a root — skip the tether
                    g.DrawLine(bonePen, screen[p], screen[i]);
                }
            }

            // ---- pass 4: draw joints (dots) ----
            using (var jointBrush = new SolidBrush(Color.FromArgb(255, 190, 60)))
            using (var rootBrush = new SolidBrush(Color.FromArgb(120, 180, 255)))
            {
                for (int i = 0; i < screen.Length; i++)
                {
                    float r = 3.5f;
                    Brush brush = parentIndices[i] < 0 ? rootBrush : jointBrush;
                    g.FillEllipse(brush, screen[i].X - r, screen[i].Y - r, r * 2, r * 2);
                }
            }

            // ---- overlay: frame / time readout ----
            DrawReadout(g, w, h);
        }

        private void DrawReadout(Graphics g, int w, int h)
        {
            if (nom == null) return;
            float duration = nom.frameRate > 0 ? nom.frameCount / nom.frameRate : 0f;
            int frame = (int)(currentTime * nom.frameRate);

            string text = string.Format("Frame {0} / {1}    {2:0.00}s / {3:0.00}s",
                frame, nom.frameCount, currentTime, duration);

            using (var brush = new SolidBrush(Color.FromArgb(180, 180, 200)))
            using (var font = new Font(FontFamily.GenericSansSerif, 8.5f))
            {
                SizeF size = g.MeasureString(text, font);
                g.DrawString(text, font, brush, 8, h - size.Height - 6);
            }
        }

        private void DrawCenteredHint(Graphics g, int w, int h, string message)
        {
            using (var brush = new SolidBrush(Color.FromArgb(150, 150, 165)))
            using (var font = new Font(FontFamily.GenericSansSerif, 10f))
            {
                SizeF size = g.MeasureString(message, font);
                g.DrawString(message, font, brush,
                    (w - size.Width) * 0.5f, (h - size.Height) * 0.5f);
            }
        }

        /// <summary>
        /// A Panel subclass with double buffering switched on. Drawing the
        /// skeleton every ~16 ms on a plain Panel would flicker badly;
        /// double buffering composites off-screen and blits once per paint.
        /// </summary>
        private sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.OptimizedDoubleBuffer
                            | ControlStyles.AllPaintingInWmPaint
                            | ControlStyles.UserPaint, true);
            }
        }
    }
}