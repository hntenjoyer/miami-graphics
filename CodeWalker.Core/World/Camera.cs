using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeWalker.World
{
    public class Camera
    {
        public Vector3 TargetRotation = Vector3.Zero;
        public Vector3 CurrentRotation = Vector3.Zero;
        public float Smoothness;
        public float Sensitivity;
        public float TargetDistance = 1.0f;
        public float CurrentDistance = 1.0f;
        public float ZoomCurrentTime = 0.0f;
        public float ZoomTargetTime = 2.0f;
        public float ZoomVelocity = 0.0f;
        public float ZoomSpeed = 0.1f;
        public float Width = 1920.0f;
        public float Height = 1080.0f;
        public float FieldOfView;
        public float FieldOfViewFactor = 0.5f / (float)Math.Tan( 1.0f * 0.5f);
        public float AspectRatio = 1920.0f / 1080.0f;
        public float ZNear = 0.01f;
        public float ZFar = 100000.0f;
        public Entity FollowEntity = null;
        public Vector3 LocalLookAt = Vector3.ForwardLH;
        public float VOffset = 0.0f;
        public bool UpdateProj = true;
        public bool IsMapView = false;
        public bool IsOrthographic = false;
        public float OrthographicSize = 20.0f;
        public float OrthographicTargetSize = 20.0f;
        public Matrix ProjMatrix = Matrix.Identity;
        public Vector3 Position = Vector3.Zero;
        public Vector3 UpDirection = Vector3.Up;
        public Vector3 ViewDirection = Vector3.ForwardLH;
        public Quaternion ViewQuaternion = Quaternion.Identity;
        public Quaternion ViewInvQuaternion = Quaternion.Identity;
        public Matrix ViewMatrix = Matrix.Identity;
        public Matrix ViewInvMatrix = Matrix.Identity;
        public Matrix ViewProjMatrix = Matrix.Identity;
        public Matrix ViewProjInvMatrix = Matrix.Identity;
        public Frustum ViewFrustum = new Frustum();
        public Vector3 MouseRayNear = Vector3.Zero;
        public Vector3 MouseRayFar = Vector3.Zero;
        public Ray MouseRay;
        private float MouseX = 0;
        private float MouseY = 0;
        private object syncRoot = new object();

        public Camera(float smoothness, float sensitivity, float fov)
        {
            Smoothness = smoothness;
            Sensitivity = sensitivity;
            FieldOfView = fov;
            FieldOfViewFactor = 0.5f / (float)Math.Tan(FieldOfView * 0.5f);
        }

        public void SetMousePosition(int x, int y)
        {
            MouseX = (x / Width) * 2.0f - 1.0f;
            MouseY = (y / Height) * -2.0f + 1.0f;
        }

        public void SetFollowEntity(Entity e)
        {
            FollowEntity = e;
        }

        public void Update(float elapsed)
        {
            lock (syncRoot)
            {
                UpdateFollow(elapsed);
                if (UpdateProj) UpdateProjMatrix();

                UpdateProjection();
            }
        }
        private void UpdateFollow(float elapsed)
        {
            const float ythresh = 1.55f;
            const float nythresh = -1.55f;
            Vector3 up = Vector3.Up;
            if (TargetRotation.Y > ythresh) TargetRotation.Y = ythresh;
            if (TargetRotation.Y < nythresh) TargetRotation.Y = nythresh;
            float sv = Math.Min(Smoothness * elapsed, 1.0f);
            CurrentRotation = CurrentRotation + ((TargetRotation - CurrentRotation) * sv);

            if (TargetDistance > 11000.0f) TargetDistance = 11000.0f;
            if (TargetDistance < 0.0001f) TargetDistance = 0.0001f;
            ZoomCurrentTime += elapsed;
            if (ZoomCurrentTime > ZoomTargetTime) ZoomCurrentTime = ZoomTargetTime;
            float currentTime = ZoomCurrentTime / ZoomTargetTime;
            float deltaDist = TargetDistance - CurrentDistance;
            if (currentTime < 1.0f && deltaDist > 0.0f)
            {
                float y = currentTime*currentTime*currentTime;
                deltaDist *= y;
            }
            CurrentDistance = CurrentDistance + deltaDist * ZoomSpeed;

            if (IsOrthographic || IsMapView)
            {
                if (OrthographicTargetSize > 20000.0f) OrthographicTargetSize = 20000.0f;
                if (OrthographicTargetSize < 1.0f) OrthographicTargetSize = 1.0f;
                OrthographicSize = OrthographicSize + ((OrthographicTargetSize - OrthographicSize) * sv);
                UpdateProj = true;
            }

            if (IsMapView)
            {

                Vector3 cpos = new Vector3();
                if (FollowEntity != null)
                {
                    cpos = FollowEntity.Position;
                }
                LocalLookAt = Vector3.Zero;
                Position = cpos;
                Position.Z = 1000.0f;
                ViewDirection = -Vector3.UnitZ;
                UpDirection = Vector3.UnitY;
            }
            else
            {

                Vector3 rdir = new Vector3();
                float cryd = (float)Math.Cos(CurrentRotation.Y);
                rdir.X = -(float)Math.Sin(-CurrentRotation.X) * cryd;
                rdir.Z = -(float)Math.Cos(-CurrentRotation.X) * cryd;
                rdir.Y = (float)Math.Sin(CurrentRotation.Y);
                Vector3 lookat = new Vector3(0.0f, VOffset, 0.0f);
                Vector3 cpos = new Vector3();
                if (FollowEntity != null)
                {
                    up = FollowEntity.Orientation.Multiply(up);
                    lookat = FollowEntity.Orientation.Multiply(lookat);
                    rdir = FollowEntity.Orientation.Multiply(rdir);
                    cpos = FollowEntity.Position;
                }
                LocalLookAt = (rdir * CurrentDistance) + lookat;
                Position = cpos + LocalLookAt;
                ViewDirection = Vector3.Normalize(-rdir);
                UpDirection = up;
            }

            ViewQuaternion = Quaternion.LookAtRH(Vector3.Zero, ViewDirection, UpDirection);
            ViewInvQuaternion = Quaternion.Invert(ViewQuaternion);
            ViewMatrix = ViewQuaternion.ToMatrix();
            ViewInvMatrix = Matrix.Invert(ViewMatrix);

        }
        private void UpdateProjMatrix()
        {
            if (IsMapView)
            {
                ProjMatrix = Matrix.OrthoRH(AspectRatio * OrthographicSize, OrthographicSize, 3000.0f, 1.0f);
            }
            else if (IsOrthographic)
            {
                ProjMatrix = Matrix.OrthoRH(AspectRatio * OrthographicSize, OrthographicSize, ZFar, ZNear);
            }
            else
            {
                ProjMatrix = Matrix.PerspectiveFovRH(FieldOfView, AspectRatio, ZFar, ZNear);
            }
            UpdateProj = false;
        }
        private void UpdateProjection()
        {
            float mx = MouseX;
            float my = MouseY;
            ViewProjMatrix = Matrix.Multiply(ViewMatrix, ProjMatrix);
            ViewProjInvMatrix = Matrix.Invert(ViewProjMatrix);
            MouseRayNear = ViewProjInvMatrix.MultiplyW(new Vector3(mx, my, 1.0f));
            MouseRayFar = ViewProjInvMatrix.MultiplyW(new Vector3(mx, my, 0.0f));
            MouseRay.Position = Vector3.Zero;
            MouseRay.Direction = Vector3.Normalize(MouseRayFar - MouseRayNear);
            if (IsMapView || IsOrthographic)
            {
                MouseRay.Position = MouseRayNear;
            }
            ViewFrustum.Update(ref ViewProjMatrix);
            ViewFrustum.Position = Position;
        }
        private void UpdateMousedItem()
        {
        }
        public void OnWindowResize(int w, int h)
        {
            lock (syncRoot)
            {
                Width = (float)w;
                Height = (float)h;
                AspectRatio = Width / Height;
                UpdateProj = true;
            }
        }

        public void ControllerRotate(float x, float y, float elapsed)
        {
            lock (syncRoot)
            {
                TargetRotation.X += x*elapsed;
                TargetRotation.Y += y*elapsed;
            }
        }

        public void ControllerZoom(float z)
        {
            lock (syncRoot)
            {
                float v = (z < 0) ? (1.0f - z) : (z > 0) ? (1.0f / (1.0f + z)) : 1.0f;
                TargetDistance *= v;
                OrthographicTargetSize *= v;
            }
        }

        public void MouseRotate(int x, int y)
        {
            lock (syncRoot)
            {
                TargetRotation.X += x * Sensitivity;
                TargetRotation.Y += y * Sensitivity;
            }
        }

        public void MouseZoom(int z)
        {
            lock (syncRoot)
            {
                float v = (z < 0) ? 1.1f : (z > 0) ? 1.0f / 1.1f : 1.0f;
                TargetDistance *= v;
                OrthographicTargetSize *= v;
            }
        }

    }

    public class Frustum
    {
        public Plane[] Planes = new Plane[6];
        public Vector3 Position;

        public void Update(ref Matrix vp)
        {
            Planes[0] = Plane.Normalize(new Plane((vp.M14 + vp.M11), (vp.M24 + vp.M21), (vp.M34 + vp.M31), (vp.M44 + vp.M41)));
            Planes[1] = Plane.Normalize(new Plane((vp.M14 - vp.M11), (vp.M24 - vp.M21), (vp.M34 - vp.M31), (vp.M44 - vp.M41)));
            Planes[2] = Plane.Normalize(new Plane((vp.M14 - vp.M12), (vp.M24 - vp.M22), (vp.M34 - vp.M32), (vp.M44 - vp.M42)));
            Planes[3] = Plane.Normalize(new Plane((vp.M14 + vp.M12), (vp.M24 + vp.M22), (vp.M34 + vp.M32), (vp.M44 + vp.M42)));
            Planes[4] = Plane.Normalize(new Plane((vp.M14 - vp.M13), (vp.M24 - vp.M23), (vp.M34 - vp.M33), (vp.M44 - vp.M43)));
            Planes[5] = Plane.Normalize(new Plane((vp.M13), (vp.M23), (vp.M33), 0.0f));
        }

        public bool ContainsSphereNoClipNoOpt(ref Vector3 c, float r)
        {
            float nr = -r;
            for (int i = 0; i < 5; i++)
            {
                if (Plane.DotCoordinate(Planes[i], c) < nr)
                {
                    return false;
                }
            }
            return true;
        }
        public bool ContainsAABBNoClip(ref Vector3 cen, ref Vector3 e)
        {
            var c = cen - Position;
            for (int i = 0; i < 5; i++)
            {
                var pn = Planes[i].Normal;
                var d = (c.X * pn.X) + (c.Y * pn.Y) + (c.Z * pn.Z);
                var r = (e.X * (pn.X > 0 ? pn.X : -pn.X)) + (e.Y * (pn.Y > 0 ? pn.Y : -pn.Y)) + (e.Z * (pn.Z > 0 ? pn.Z : -pn.Z));
                if ((d + r) < 0) return false;
            }
            return true;
        }
        public bool ContainsAABBNoClipNoOpt(ref Vector3 bmin, ref Vector3 bmax)
        {
            var c = (bmax + bmin) * 0.5f - Position;
            var e = (bmax - bmin) * 0.5f;
            for (int i = 0; i < 5; i++)
            {
                var pd = Planes[i].D;
                var pn = Planes[i].Normal;
                var d = (c.X * pn.X) + (c.Y * pn.Y) + (c.Z * pn.Z);
                var r = (e.X * (pn.X > 0 ? pn.X : -pn.X)) + (e.Y * (pn.Y > 0 ? pn.Y : -pn.Y)) + (e.Z * (pn.Z > 0 ? pn.Z : -pn.Z));
                if ((d + r) < -pd) return false;
            }
            return true;
        }
        public bool ContainsAABBNoFrontClipNoOpt(ref Vector3 bmin, ref Vector3 bmax)
        {
            var c = (bmax + bmin) * 0.5f - Position;
            var e = (bmax - bmin) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                var pd = Planes[i].D;
                var pn = Planes[i].Normal;
                var d = (c.X * pn.X) + (c.Y * pn.Y) + (c.Z * pn.Z);
                var r = (e.X * (pn.X > 0 ? pn.X : -pn.X)) + (e.Y * (pn.Y > 0 ? pn.Y : -pn.Y)) + (e.Z * (pn.Z > 0 ? pn.Z : -pn.Z));
                if ((d + r) < -pd) return false;
            }
            return true;
        }

    }
}
