using System.Collections.Generic;

namespace PSULib.FileClasses.Models
{
    /// <summary>
    /// The standard ("vanilla") PSU player skeleton, baked in as a hardcoded
    /// bone table so NOM-to-GLB export has a sensible default rest pose with
    /// no external file dependency.
    ///
    /// The values were extracted from a real PSU character XNJ
    /// (np_xxx_900_mh.xnj) - the same 28-bone humanoid skeleton every PSU
    /// player character shares. Translation is in PSU units; rotation is
    /// given both as radians and degrees (per-axis Euler, XZY order - see
    /// XnjBone.RotationOrderNote); scale is unit for every bone.
    ///
    /// Values below ~1e-5 in the source file were floating-point noise
    /// (rounding artifacts in the game's data) and have been snapped to
    /// exactly zero here for cleanliness; this is well below any visible
    /// threshold.
    ///
    /// <see cref="Create"/> returns a fully-populated <see cref="XnjFile"/>
    /// so the exporter can treat the vanilla skeleton and a user-supplied
    /// XNJ through the exact same code path.
    /// </summary>
    public static class VanillaPsuSkeleton
    {
        /// <summary>Number of bones in the standard PSU player skeleton.</summary>
        public const int BoneCount = 28;

        /// <summary>
        /// Build an <see cref="XnjFile"/> populated with the vanilla PSU
        /// player skeleton. Each call returns a fresh instance - callers
        /// are free to mutate the result without affecting anyone else.
        /// </summary>
        public static XnjFile Create()
        {
            var xnj = new XnjFile();

            // Bone table: each AddBone call is one bone, in file order.
            // Args: index, parentIndex,
            //       transX, transY, transZ,
            //       rotRadX, rotRadY, rotRadZ,
            //       rotDegX, rotDegY, rotDegZ,
            //       scaleX, scaleY, scaleZ
            // (0) Root
            AddBone(xnj, 0, -1,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                1.0f, 1.0f, 1.0f);
            // (1) Navel
            AddBone(xnj, 1, 0,
                0f, 9.783877f, 0f,
                0f, -1.570796f, 0f,
                0f, -90.0f, 0f,
                1.0f, 1.0f, 1.0f);
            // (2) Pelvis
            AddBone(xnj, 2, 1,
                0f, 0f, 0f,
                -1.570796f, 0f, 1.570796f,
                -90.0f, 0f, 90.0f,
                1.0f, 0.9999999f, 1.0f);
            // (3) L_thigh
            AddBone(xnj, 3, 2,
                0f, 0.9108223f, 0f,
                -0.005752428f, 0.001821602f, 3.085315f,
                -0.3295898f, 0.1043701f, 176.7755f,
                1.0f, 0.9999998f, 1.0f);
            // (4) L_calf
            AddBone(xnj, 4, 3,
                3.910609f, 0f, 0f,
                0f, -0.09501094f, 0f,
                0f, -5.443726f, 0f,
                0.9999999f, 0.9999998f, 0.9999998f);
            // (5) L_foot
            AddBone(xnj, 5, 4,
                4.324242f, 0f, 0f,
                -0.001629855f, 0.1007634f, 0.05359345f,
                -0.09338379f, 5.773315f, 3.070679f,
                1.0f, 0.9999998f, 0.9999998f);
            // (6) R_thigh
            AddBone(xnj, 6, 2,
                0f, -0.9108223f, 0f,
                0.005752428f, 0.001821602f, -3.085315f,
                0.3295898f, 0.1043701f, -176.7755f,
                1.0f, 0.9999998f, 1.0f);
            // (7) R_calf
            AddBone(xnj, 7, 6,
                3.910609f, 0f, 0f,
                0f, -0.09501094f, 0f,
                0f, -5.443726f, 0f,
                1.0f, 0.9999998f, 0.9999999f);
            // (8) R_foot
            AddBone(xnj, 8, 7,
                4.324242f, 0f, 0f,
                0.001629855f, 0.1007634f, -0.05359345f,
                0.09338379f, 5.773315f, -3.070679f,
                1.0f, 0.9999999f, 1.0f);
            // (9) Spine
            AddBone(xnj, 9, 1,
                -0.001204682f, 1.388368f, 0f,
                -1.570796f, 0f, 1.584123f,
                -90.0f, 0f, 90.76355f,
                0.9999999f, 1.0f, 0.9999999f);
            // (10) Spine1
            AddBone(xnj, 10, 9,
                1.513309f, 0f, 0.001814195f,
                0f, -0.1865704f, 0f,
                0f, -10.6897f, 0f,
                0.9999995f, 1.0f, 0.9999999f);
            // (11) Neck_root
            AddBone(xnj, 11, 10,
                2.321016f, 0f, 0.01411372f,
                3.140826f, 0f, 1.570796f,
                179.9561f, 0f, 90.0f,
                0.9999999f, 0.9999999f, 1.0f);
            // (12) Neck
            AddBone(xnj, 12, 11,
                0f, 0f, 0f,
                -1.570796f, -1.570796f, 1.245592f,
                -90.0f, -90.0f, 71.36719f,
                1.000001f, 0.9999999f, 1.0f);
            // (13) Head
            AddBone(xnj, 13, 12,
                1.073917f, 0f, 0f,
                0f, -0.04621117f, 0f,
                0f, -2.647705f, 0f,
                0.9999999f, 0.9999999f, 0.9999998f);
            // (14) L_clavicle
            AddBone(xnj, 14, 11,
                0.3188057f, -0.00240753f, 0.01411064f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0.9999998f, 0.9999999f, 1.0f);
            // (15) L_upperarm
            AddBone(xnj, 15, 14,
                1.184069f, 0f, 0f,
                0.2911687f, 0.2126481f, -0.770346f,
                16.68274f, 12.18384f, -44.13757f,
                0.9999999f, 0.9999999f, 0.9999998f);
            // (16) L_forearm
            AddBone(xnj, 16, 15,
                2.559719f, 0f, 0f,
                0f, -0.1822561f, 0f,
                0f, -10.4425f, 0f,
                0.9999997f, 0.9999999f, 1.0f);
            // (17) L_hand
            AddBone(xnj, 17, 16,
                2.559719f, 0f, 0f,
                -1.570029f, 0f, -0.09433982f,
                -89.95605f, 0f, -5.405273f,
                0.9999993f, 1.0f, 0.9999999f);
            // (18) L_weapon
            AddBone(xnj, 18, 17,
                0.6999995f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                1.0f, 1.0f, 1.0f);
            // (19) R_clavicle
            AddBone(xnj, 19, 11,
                -0.3188054f, -0.002407504f, 0.01411324f,
                0f, 0f, 3.141593f,
                0f, 0f, 180.0f,
                0.9999999f, 0.9999999f, 0.9999998f);
            // (20) R_upperarm
            AddBone(xnj, 20, 19,
                1.184069f, 0f, 0f,
                -0.2911687f, 0.2126481f, 0.770346f,
                -16.68274f, 12.18384f, 44.13757f,
                0.9999999f, 0.9999998f, 0.9999999f);
            // (21) R_forearm
            AddBone(xnj, 21, 20,
                2.559719f, 0f, 0f,
                0f, -0.1822561f, 0f,
                0f, -10.4425f, 0f,
                0.9999998f, 0.9999996f, 0.9999998f);
            // (22) R_hand
            AddBone(xnj, 22, 21,
                2.55972f, 0f, 0f,
                1.570029f, 0f, 0.09433982f,
                89.95605f, 0f, 5.405273f,
                1.0f, 0.9999998f, 0.9999996f);
            // (23) R_weapon
            AddBone(xnj, 23, 22,
                0.6999999f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0.9999999f, 1.0f, 1.0f);
            // (24) L_breast
            AddBone(xnj, 24, 10,
                1.302263f, 0.4338791f, -0.4648326f,
                0f, -1.370995f, 2.722432f,
                0f, -78.55225f, 155.9839f,
                1.0f, 0.9999998f, 0.9999998f);
            // (25) R_breast
            AddBone(xnj, 25, 10,
                1.302263f, -0.433876f, -0.464834f,
                0f, -1.370995f, -2.722432f,
                0f, -78.55225f, -155.9839f,
                0.9999999f, 0.9999998f, 0.9999998f);
            // (26) Belly
            AddBone(xnj, 26, 9,
                0.1938738f, 0f, -0.02924503f,
                0f, -1.539637f, 3.141593f,
                0f, -88.21472f, 180.0f,
                0.9999997f, 1.0f, 0.9999998f);
            // (27) Body
            AddBone(xnj, 27, 0,
                0f, 0f, 0f,
                0f, 0f, 0f,
                0f, 0f, 0f,
                1.0f, 1.0f, 1.0f);

            return xnj;
        }

        /// <summary>
        /// Helper: construct one XnjBone from primitive values and append
        /// it to the file's bone list. Kept tiny and private - it exists
        /// only to keep the big generated table above readable.
        /// </summary>
        private static void AddBone(XnjFile xnj, int index, int parentIndex,
            float tx, float ty, float tz,
            float rRadX, float rRadY, float rRadZ,
            float rDegX, float rDegY, float rDegZ,
            float sx, float sy, float sz)
        {
            var bone = new XnjBone
            {
                Index = index,
                ParentIndex = parentIndex,
                // FirstChild / NextSibling aren't needed by the exporter
                // (it walks parents, not the child/sibling chain) so we
                // leave them at -1. WeightUsed likewise - the vanilla
                // skeleton is for animation export, not vertex skinning.
                FirstChildIndex = -1,
                NextSiblingIndex = -1,
                WeightUsed = -1,
                LocalTranslationX = tx,
                LocalTranslationY = ty,
                LocalTranslationZ = tz,
                LocalRotationRadX = rRadX,
                LocalRotationRadY = rRadY,
                LocalRotationRadZ = rRadZ,
                LocalRotationDegX = rDegX,
                LocalRotationDegY = rDegY,
                LocalRotationDegZ = rDegZ,
                LocalScaleX = sx,
                LocalScaleY = sy,
                LocalScaleZ = sz,
            };
            xnj.Bones.Add(bone);
        }
    }
}