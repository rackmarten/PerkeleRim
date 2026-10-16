using System;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // Fills PlayerState's camera fields (eye, roll, fov) from MSC's camera, in Unity terms. Unity-free,
    // so the tests cover it; the caller passes the camera's transform vectors as Vec3.
    public static class CameraPose
    {
        // a_eye: camera position minus the feet, Unity axes and meters. a_right, a_up: the camera's
        // right and up vectors (Unity world). a_fov: Camera.fieldOfView (vertical, degrees).
        public static unsafe void Fill(ref PlayerState s, Vec3 eye, Vec3 right, Vec3 up, float fov)
        {
            var e = Coords.UnityToSkyrim(eye);
            s.eye[0] = e.x;
            s.eye[1] = e.y;
            s.eye[2] = e.z;
            s.roll = Roll(right, up);
            s.fov = fov;
            s.flags |= Wire.PlayerCamera;
        }

        // Where the camera looks (Unity yaw and pitch in degrees, its transform's eulerAngles y and
        // x). With roll that is its whole rotation (Unity turns by z, then x, then y, as Skyrim's
        // Camera::Basis); the player's yaw and its camera's pitch are not, once the vehicle the
        // player sits in is rolled: looking down at the controls the view was off by degrees.
        public static void View(ref PlayerState s, float yaw, float pitch)
        {
            if (pitch > 180f) pitch -= 360f;
            s.viewHeading = Coords.UnityAngleToSkyrim(yaw);
            s.viewPitch = pitch * (float)(Math.PI / 180.0);
        }

        // MSC's chase camera is on: Fill has its pose, and this where it looks.
        public static void Chase(ref PlayerState s, float yaw, float pitch)
        {
            View(ref s, yaw, pitch);
            s.flags |= Wire.PlayerChaseCamera;
        }

        // Tilt about the view direction, positive to the right: tilting right drops the camera's
        // right vector below the horizon (Unity is left-handed, so from behind that's clockwise).
        public static float Roll(Vec3 right, Vec3 up) => (float)Math.Atan2(-right.y, up.y);
    }
}
