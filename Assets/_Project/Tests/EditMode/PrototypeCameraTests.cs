using Ediki.Core;
using Ediki.Editor.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Ediki.Tests
{
    /// <summary>
    /// The map view's projection and its inverse, in both projections.
    ///
    /// This is the one piece of the editor where being subtly wrong is invisible
    /// rather than obvious: a projection that is off by half a cell still draws a
    /// perfectly convincing board, and the only symptom is that painting lands
    /// next to the cursor. So the round trip is asserted for every cell at every
    /// angle rather than eyeballed.
    /// </summary>
    public class PrototypeCameraTests
    {
        private static readonly Rect View = new Rect(190f, 24f, 900f, 600f);

        private static MapViewCamera Camera(float pitch, float yaw, bool perspective)
        {
            MapViewCamera cam = new MapViewCamera();
            cam.SetViewRect(View);
            cam.Perspective = perspective;
            cam.Pitch = pitch;
            cam.Yaw = yaw;
            cam.FocusMap(10, 8);
            return cam;
        }

        [Test]
        public void EveryCellRoundTripsAtEveryAngle([Values(true, false)] bool perspective)
        {
            float[] pitches = { 20f, 40f, 54f, 75f, MapViewCamera.MaxPitch };
            float[] yaws = { 0f, 32f, 90f, 137f, 215f, 300f };

            foreach (float pitch in pitches)
                foreach (float yaw in yaws)
                {
                    MapViewCamera cam = Camera(pitch, yaw, perspective);

                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 10; x++)
                        {
                            Vector2 window = cam.WorldToWindow(MapViewCamera.CellToWorld(x, y));
                            Coord? back = cam.WindowToCell(window, 10, 8);

                            string where = (perspective ? "perspective" : "ortho")
                                + " pitch " + pitch + " yaw " + yaw + ": cell (" + x + "," + y + ")";

                            Assert.IsTrue(back.HasValue, where + " projected off the map.");
                            Assert.AreEqual(new Coord(x, y), back.Value, where + " picking is offset.");
                        }
                }
        }

        [Test]
        public void EveryCellRoundTripsAtEveryZoom([Values(true, false)] bool perspective)
        {
            float[] steps = { -8f, -3f, 0f, 4f, 9f };

            foreach (float step in steps)
            {
                MapViewCamera cam = Camera(54f, 32f, perspective);
                cam.ZoomBy(step, View.center);

                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 10; x++)
                    {
                        Vector2 window = cam.WorldToWindow(MapViewCamera.CellToWorld(x, y));
                        Coord? back = cam.WindowToCell(window, 10, 8);

                        Assert.IsTrue(back.HasValue && back.Value == new Coord(x, y),
                            (perspective ? "perspective" : "ortho") + " zoom step " + step
                            + ": cell (" + x + "," + y + ") did not round-trip.");
                    }
            }
        }

        [Test]
        public void PanKeepsTheWorldPointUnderTheCursor([Values(true, false)] bool perspective)
        {
            MapViewCamera cam = Camera(54f, 32f, perspective);

            Vector2 from = new Vector2(400f, 300f);
            Vector2 to = new Vector2(520f, 250f);

            Vector3 grabbed = cam.WindowToGround(from);
            cam.Pan(from, to);

            Assert.Less((grabbed - cam.WindowToGround(to)).magnitude, 0.002f,
                "The map slipped under the cursor while panning.");
        }

        [Test]
        public void ZoomKeepsThePointUnderTheCursorStill([Values(true, false)] bool perspective)
        {
            MapViewCamera cam = Camera(54f, 32f, perspective);
            Vector2 cursor = new Vector2(700f, 420f);

            Vector3 before = cam.WindowToGround(cursor);
            cam.ZoomBy(-3f, cursor);

            Assert.Less((before - cam.WindowToGround(cursor)).magnitude, 0.002f,
                "Zooming drifted the point under the cursor.");
        }

        [Test]
        public void TopViewPutsCellZeroZeroAtTheTopLeft()
        {
            // Same orientation as BattleView, which draws cell (x, y) at
            // (x, h, -y) with the camera looking straight down. If these two ever
            // disagree, a map authored here would play mirrored.
            MapViewCamera cam = new MapViewCamera();
            cam.SetViewRect(View);
            cam.TopView(10, 8);

            Vector2 origin = cam.WorldToWindow(MapViewCamera.CellToWorld(0, 0));
            Vector2 far = cam.WorldToWindow(MapViewCamera.CellToWorld(9, 7));

            Assert.Less(origin.y, far.y, "y = 0 must be at the TOP of the screen.");
            Assert.Less(origin.x, far.x, "x = 0 must be on the LEFT of the screen.");
            Assert.IsFalse(cam.Perspective, "The layout view must be orthographic to measure with.");
        }

        [Test]
        public void ClicksOutsideTheMapReportNoCell()
        {
            MapViewCamera cam = new MapViewCamera();
            cam.SetViewRect(View);
            cam.TopView(10, 8);

            Vector2 off = cam.WorldToWindow(MapViewCamera.CellToWorld(-6, -6));
            Assert.IsFalse(cam.WindowToCell(off, 10, 8).HasValue,
                "A click off the board must return nothing, not clamp to an edge cell.");
        }

        [Test]
        public void LookingAtTheHorizonReportsNoGroundRatherThanAWildCoordinate()
        {
            // Near-flat pitch sends the upper half of the view past the horizon.
            // The ray misses the ground entirely there, and the honest answer is
            // "nothing", not a cell thousands of units away.
            MapViewCamera cam = Camera(20f, 0f, true);
            cam.Pitch = 1f;

            Vector3 ground;
            bool hit = cam.TryWindowToGround(new Vector2(View.center.x, View.y + 4f), out ground);

            Assert.IsFalse(hit, "A ray aimed above the horizon reported a ground hit at " + ground + ".");
        }

        [Test]
        public void OrbitClampsPitchToTheUsableRange()
        {
            // The ground intersection divides by the ray's vertical component, so
            // a view direction parallel to the ground has no answer at all.
            MapViewCamera cam = Camera(54f, 32f, true);

            cam.Orbit(new Vector2(0f, 10000f));
            Assert.GreaterOrEqual(cam.Pitch, MapViewCamera.MinPitch);

            cam.Orbit(new Vector2(0f, -10000f));
            Assert.LessOrEqual(cam.Pitch, MapViewCamera.MaxPitch);
        }

        [Test]
        public void FocusMapFitsTheWholeBoardInsideTheView([Values(true, false)] bool perspective)
        {
            MapViewCamera cam = new MapViewCamera();
            cam.SetViewRect(View);
            cam.Perspective = perspective;
            cam.Pitch = 54f;
            cam.Yaw = 32f;
            cam.FocusMap(24, 16);

            for (int i = 0; i < 4; i++)
            {
                int x = (i & 1) == 0 ? 0 : 23;
                int y = (i & 2) == 0 ? 0 : 15;
                Vector2 p = cam.WorldToWindow(MapViewCamera.CellToWorld(x, y));

                Assert.IsTrue(View.Contains(p),
                    (perspective ? "perspective" : "ortho") + ": corner (" + x + "," + y
                    + ") landed outside the view at " + p + ".");
            }
        }

        [Test]
        public void TogglingProjectionKeepsThePictureRoughlyTheSameSize()
        {
            // Flipping projection should read as a change of projection, not as
            // the camera jumping somewhere else.
            MapViewCamera cam = Camera(54f, 32f, true);

            Vector2 before = cam.WorldToWindow(MapViewCamera.CellToWorld(9, 7));
            cam.TogglePerspective();
            Vector2 after = cam.WorldToWindow(MapViewCamera.CellToWorld(9, 7));

            Assert.IsFalse(cam.Perspective);
            Assert.Less((before - after).magnitude, 90f,
                "The far corner jumped " + (before - after).magnitude + " points on a projection toggle.");
        }

        [Test]
        public void FlyMovesThePivotAlongTheViewDirection()
        {
            MapViewCamera cam = Camera(54f, 0f, true);
            Vector3 start = cam.Pivot;

            cam.Fly(0f, 1f, 0f, 0.5f, false);

            Vector3 moved = cam.Pivot - start;
            Assert.Greater(moved.magnitude, 0.1f, "W did not move the camera.");
            Assert.Greater(Vector3.Dot(moved.normalized, cam.Forward), 0.9f,
                "W moved the camera somewhere other than forward.");

            // Q and E are world vertical, so you can rise without tilting.
            Vector3 before = cam.Pivot;
            cam.Fly(0f, 0f, 1f, 0.5f, false);
            Vector3 lift = cam.Pivot - before;
            Assert.Greater(lift.y, 0.1f);
            Assert.Less(Mathf.Abs(lift.x) + Mathf.Abs(lift.z), 0.001f, "E drifted sideways.");
        }

        [Test]
        public void FlyIsFasterWithShiftAndScalesWithHowFarOutYouAre()
        {
            MapViewCamera near = Camera(54f, 0f, true);
            near.Distance = 8f;
            Vector3 nearStart = near.Pivot;
            near.Fly(0f, 1f, 0f, 0.5f, false);
            float slow = (near.Pivot - nearStart).magnitude;

            MapViewCamera fast = Camera(54f, 0f, true);
            fast.Distance = 8f;
            Vector3 fastStart = fast.Pivot;
            fast.Fly(0f, 1f, 0f, 0.5f, true);

            Assert.Greater((fast.Pivot - fastStart).magnitude, slow, "Shift did not speed flight up.");

            MapViewCamera farOut = Camera(54f, 0f, true);
            farOut.Distance = 50f;
            Vector3 farStart = farOut.Pivot;
            farOut.Fly(0f, 1f, 0f, 0.5f, false);

            Assert.Greater((farOut.Pivot - farStart).magnitude, slow,
                "Flight speed should scale with how far out the camera is.");
        }
    }
}
