using UnityEngine;

namespace SpaceWeave.Output
{
    public enum SpaceWeaveOutputMode
    {
        Equirectangular,
        Cylindrical,
        CubemapCross,
        CubemapStrip,
        Fisheye180,
        Fisheye360,
        Eac,
        // Reserved: Unity outputs black; SpaceWeave must not treat this as valid content.
        SurfaceAtlasPlaceholder
    }

    public static class SpaceWeaveOutputContract
    {
        public enum CubemapFace
        {
            PositiveX,
            NegativeX,
            PositiveY,
            NegativeY,
            PositiveZ,
            NegativeZ
        }

        public static bool IsImplemented(SpaceWeaveOutputMode mode)
        {
            return mode != SpaceWeaveOutputMode.SurfaceAtlasPlaceholder;
        }

        public static Vector2Int RequiredSize(
            SpaceWeaveOutputMode mode, int panoramaWidth, int cubemapFaceSize)
        {
            panoramaWidth = Mathf.Max(2, panoramaWidth + (panoramaWidth & 1));
            cubemapFaceSize = Mathf.Max(16, cubemapFaceSize);
            switch (mode)
            {
                case SpaceWeaveOutputMode.CubemapCross:
                    return new Vector2Int(cubemapFaceSize * 4, cubemapFaceSize * 3);
                case SpaceWeaveOutputMode.CubemapStrip:
                    return new Vector2Int(cubemapFaceSize * 6, cubemapFaceSize);
                case SpaceWeaveOutputMode.Eac:
                    return new Vector2Int(cubemapFaceSize * 3, cubemapFaceSize * 2);
                case SpaceWeaveOutputMode.Fisheye180:
                case SpaceWeaveOutputMode.Fisheye360:
                    return new Vector2Int(panoramaWidth, panoramaWidth);
                case SpaceWeaveOutputMode.SurfaceAtlasPlaceholder:
                    return new Vector2Int(panoramaWidth, panoramaWidth / 2);
                default:
                    return new Vector2Int(panoramaWidth, panoramaWidth / 2);
            }
        }

        public static string SenderSuffix(SpaceWeaveOutputMode mode)
        {
            switch (mode)
            {
                case SpaceWeaveOutputMode.Equirectangular: return "EQUIRECT";
                case SpaceWeaveOutputMode.Cylindrical: return "CYLINDRICAL";
                case SpaceWeaveOutputMode.CubemapCross: return "CUBEMAP_CROSS";
                case SpaceWeaveOutputMode.CubemapStrip: return "CUBEMAP_STRIP";
                case SpaceWeaveOutputMode.Fisheye180: return "FISHEYE_180";
                case SpaceWeaveOutputMode.Fisheye360: return "FISHEYE_360";
                case SpaceWeaveOutputMode.Eac: return "EAC";
                default: return "SURFACE_ATLAS_UNSUPPORTED";
            }
        }

        public static string SpaceWeaveInputFormatHint(SpaceWeaveOutputMode mode)
        {
            switch (mode)
            {
                case SpaceWeaveOutputMode.Equirectangular: return "Equirect";
                case SpaceWeaveOutputMode.Cylindrical: return "Cylindrical";
                case SpaceWeaveOutputMode.CubemapCross: return "Cube Cross";
                case SpaceWeaveOutputMode.CubemapStrip: return "Cube Strip";
                case SpaceWeaveOutputMode.Fisheye180: return "Fisheye 180";
                case SpaceWeaveOutputMode.Fisheye360: return "Fisheye 360";
                case SpaceWeaveOutputMode.Eac: return "EAC";
                default: return "(unsupported — pick Equirect/Cube/Fisheye/EAC)";
            }
        }

        public static Vector2 DirectionToEquirectUv(Vector3 direction)
        {
            direction.Normalize();
            float yaw = Mathf.Atan2(direction.x, direction.z);
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f));
            return new Vector2(Mathf.Repeat(yaw / (2f * Mathf.PI) + 0.5f, 1f),
                0.5f - pitch / Mathf.PI);
        }

        public static Vector2 DirectionToCylindricalUv(
            Vector3 direction, float horizontalFov, float verticalFov)
        {
            direction.Normalize();
            float yaw = Mathf.Atan2(direction.x, direction.z);
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f));
            float halfH = Mathf.Clamp(horizontalFov, 30f, 360f) * 0.5f * Mathf.Deg2Rad;
            float halfV = Mathf.Clamp(verticalFov, 30f, 179f) * 0.5f * Mathf.Deg2Rad;
            float u = horizontalFov >= 359.5f
                ? Mathf.Repeat(yaw / (2f * Mathf.PI) + 0.5f, 1f)
                : Mathf.Clamp01(yaw / (2f * halfH) + 0.5f);
            float vBottomUp = Mathf.Clamp01(
                0.5f + Mathf.Tan(pitch) / Mathf.Tan(halfV) * 0.5f);
            return new Vector2(u, 1f - vBottomUp);
        }

        public static CubemapFace DirectionToCubemapFaceUv(Vector3 direction, out Vector2 uv)
        {
            direction.Normalize();
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);
            if (ax >= ay && ax >= az)
            {
                if (direction.x >= 0f)
                {
                    uv = new Vector2((-direction.z / ax + 1f) * 0.5f,
                        1f - (direction.y / ax + 1f) * 0.5f);
                    return CubemapFace.PositiveX;
                }
                uv = new Vector2((direction.z / ax + 1f) * 0.5f,
                    1f - (direction.y / ax + 1f) * 0.5f);
                return CubemapFace.NegativeX;
            }
            if (ay >= ax && ay >= az)
            {
                if (direction.y >= 0f)
                {
                    uv = new Vector2((direction.x / ay + 1f) * 0.5f,
                        (-direction.z / ay + 1f) * 0.5f);
                    return CubemapFace.PositiveY;
                }
                uv = new Vector2((direction.x / ay + 1f) * 0.5f,
                    (direction.z / ay + 1f) * 0.5f);
                return CubemapFace.NegativeY;
            }
            if (direction.z >= 0f)
            {
                uv = new Vector2((direction.x / az + 1f) * 0.5f,
                    1f - (direction.y / az + 1f) * 0.5f);
                return CubemapFace.PositiveZ;
            }
            uv = new Vector2((-direction.x / az + 1f) * 0.5f,
                1f - (direction.y / az + 1f) * 0.5f);
            return CubemapFace.NegativeZ;
        }

        public static Vector2 DirectionToCubemapPackedUv(Vector3 direction, bool strip)
        {
            Vector2 faceUv;
            CubemapFace face = DirectionToCubemapFaceUv(direction, out faceUv);
            int faceIndex = (int)face;
            if (strip)
                return new Vector2((faceIndex + faceUv.x) / 6f, faceUv.y);

            int col;
            int row;
            switch (face)
            {
                case CubemapFace.PositiveX: col = 2; row = 1; break;
                case CubemapFace.NegativeX: col = 0; row = 1; break;
                case CubemapFace.PositiveY: col = 1; row = 0; break;
                case CubemapFace.NegativeY: col = 1; row = 2; break;
                case CubemapFace.PositiveZ: col = 1; row = 1; break;
                default: col = 3; row = 1; break;
            }
            return new Vector2((col + faceUv.x) / 4f, (row + faceUv.y) / 3f);
        }
    }
}
