using CodeWalker.GameFiles;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeWalker.World
{
    public class Entity
    {
        public Space Space;
        public YmapEntityDef EntityDef;

        public float Radius;
        public Vector3 Center;
        public Vector3 Position;
        public Quaternion Orientation = Quaternion.Identity;
        public Quaternion OrientationInv = Quaternion.Identity;

        public float Mass;
        public Matrix MomentOfInertia;
        public Vector3 Momentum;
        public Vector3 Velocity;
        public Vector3 AngularMomentum;
        public Vector3 AngularVelocity;

        public bool WasColliding;

        public bool EnableCollisions;
        public bool Enabled;

        public float Lifetime;
        public float Age;

        public virtual void PreUpdate(float elapsed)
        { }

    }

    public class PedEntity : Entity
    {

        public Vector2 ControlMovement;
        public bool ControlJump;
        public bool ControlBoost;

        public Vector3 ForwardVec;

        public Quaternion CameraOrientation = Quaternion.LookAtLH(Vector3.Zero, Vector3.Up, Vector3.ForwardLH);
        public Entity CameraEntity = new Entity();

        public bool OnGround = false;

        public PedEntity()
        {
            Radius = 0.5f;
            Center = new Vector3(0.0f, 0.0f, -1.2f);

            Mass = 80.0f;

            ForwardVec = Vector3.UnitY;
            CameraEntity.Orientation = CameraOrientation;
            CameraEntity.OrientationInv = Quaternion.Invert(Orientation);
        }

        public override void PreUpdate(float elapsed)
        {

            float movspd = 10.0f;
            float velspd = 10.0f;
            float jmpvel = 3.0f;
            float boostmult = 10.0f;
            if (ControlBoost) movspd *= boostmult;

            Quaternion rot = Quaternion.Identity;
            Quaternion ori = Quaternion.Multiply(Orientation, rot);
            SetOrientation(ori);

            float jmpamt = (ControlJump ? jmpvel : 0);
            Vector3 curvel = Velocity;
            Vector3 controlvel = new Vector3(ControlMovement * movspd, jmpamt);
            Vector3 targetvel = controlvel + new Vector3(0, 0, curvel.Z);
            Vector3 newvel = curvel + (targetvel - curvel) * velspd * elapsed;
            Velocity = newvel;
            var coll = Space.FindFirstCollision(this, elapsed);
            if (coll.Hit)
            {
                Vector3 collpos = coll.PrePos;
                Vector3 disp = Velocity * elapsed;
                Vector3 oldpos = Position;
                Vector3 targetpos = Position + disp;
                float displ = disp.Length();

                if ((disp.Z > -0.25f))
                {
                    Vector3 raydir = new Vector3(0.0f, 0.0f, -1.0f);
                    Vector3 rayoff = new Vector3(0.0f, 0.0f, 0.0f);
                    Ray ray = new Ray(targetpos + Center + rayoff, raydir);
                    var rayhit = Space.RayIntersect(ray, 1.0f);
                    if (rayhit.Hit)
                    {
                        if (rayhit.HitDist > 0)
                        {
                            Position = rayhit.Position - Center + new Vector3(0, 0, Radius);
                        }
                        else
                        {
                            Position = collpos;
                        }
                    }
                    else
                    {
                        Position = targetpos;
                    }
                }
                else
                {
                    Position = collpos;
                }

                bool wasOnGround = OnGround;
                OnGround = (Vector3.Dot(coll.SphereHit.Normal, Vector3.UnitZ) > 0.8f);
                if (OnGround)
                {
                }

                Vector3 findisp = Position - oldpos;
                float findispl = findisp.Length();
                float fdisp = Math.Min(displ, findispl);
                Vector3 dispdir = findisp / Math.Max(findispl, 0.0001f);
                float absvel = fdisp / Math.Max(elapsed, 0.0001f);
                Velocity = dispdir * absvel;

            }
            else
            {
                Position = coll.HitPos;
                OnGround = false;

                var raydir = new Vector3(0.0f, 0.0f, -1.0f);
                var ray = new Ray(Position, raydir);
                var rayhit = Space.RayIntersect(ray, float.MaxValue);
                if (!rayhit.Hit && rayhit.TestComplete)
                {
                    ray.Position = Position + new Vector3(0.0f, 0.0f, 1000.0f);
                    rayhit = Space.RayIntersect(ray, float.MaxValue);
                    if (rayhit.Hit)
                    {
                        Position = rayhit.Position + new Vector3(0.0f, 0.0f, Radius) - Center;
                        OnGround = true;
                    }
                    else
                    { }
                }
            }

            CameraEntity.Position = Position;
        }

        private void SetOrientation(Quaternion ori)
        {
            Orientation = ori;
            OrientationInv = Quaternion.Invert(Orientation);
            CameraEntity.Orientation = Quaternion.Multiply(Orientation, CameraOrientation);
            CameraEntity.OrientationInv = Quaternion.Invert(Orientation);
        }

    }

}
