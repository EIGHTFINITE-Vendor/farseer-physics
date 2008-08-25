using FarseerGames.FarseerPhysics.Mathematics;
using Microsoft.Xna.Framework;

namespace FarseerGames.FarseerPhysics.Dynamics.Springs
{
    public class FixedLinearSpring : Spring
    {
        internal Vector2 bodyAttachPoint;
        private Vector2 _difference = Vector2.Zero;

        internal Vector2 worldAttachPoint;

        public FixedLinearSpring()
        {
            Breakpoint = float.MaxValue;
        }

        public FixedLinearSpring(Body body, Vector2 _bodyAttachPoint, Vector2 worldAttachPoint, float springConstant,
                                 float dampningConstant)
        {
            Breakpoint = float.MaxValue;
            Body = body;
            bodyAttachPoint = _bodyAttachPoint;
            this.worldAttachPoint = worldAttachPoint;
            SpringConstant = springConstant;
            DampningConstant = dampningConstant;
            _difference = worldAttachPoint - Body.GetWorldPosition(_bodyAttachPoint);
            RestLength = _difference.Length();
        }

        public Body Body { get; set; }

        public Vector2 Position
        {
            get { return worldAttachPoint; }
            set { worldAttachPoint = value; }
        }

        public Vector2 BodyAttachPoint
        {
            get { return bodyAttachPoint; }
            set { bodyAttachPoint = value; }
        }

        public Vector2 WorldAttachPoint
        {
            get { return worldAttachPoint; }
            set { worldAttachPoint = value; }
        }

        public float RestLength { get; set; }

        public override void Validate()
        {
            //if either of the joint's connected bodies are disposed then dispose the joint.
            if (Body.IsDisposed)
            {
                Dispose();
            }
        }

        public override void Update(float dt)
        {
            base.Update(dt);

            if (IsDisposed) return;
            if (Body.isStatic) return;

            //calculate and apply spring force
            //F = -{s(L-r) + d[(v1-v2).L]/l}L/l   : s=spring const, d = dampning const, L=difference vector (p1-p2), l = difference magnitude, r = rest length,            
            Body.GetWorldPosition(ref bodyAttachPoint, out _bodyWorldPoint);
            Vector2.Subtract(ref _bodyWorldPoint, ref worldAttachPoint, out _difference);
            float differenceMagnitude = _difference.Length();
            if (differenceMagnitude < _epsilon)
            {
                return;
            } //if already close to rest length then return

            //calculate spring force (kX)
            Error = differenceMagnitude - RestLength;
            Vector2.Normalize(ref _difference, out _differenceNormalized);
            _springForce = SpringConstant*Error; //kX

            //calculate relative velocity 
            Body.GetVelocityAtLocalPoint(ref bodyAttachPoint, out _bodyVelocity);

            //calculate dampning force (bV)
            Vector2.Dot(ref _bodyVelocity, ref _difference, out _temp);
            _dampningForce = DampningConstant*_temp/differenceMagnitude; //bV     

            //calculate final force (spring + dampning)
            Vector2.Multiply(ref _differenceNormalized, -(_springForce + _dampningForce), out _force);

            Body.ApplyForceAtLocalPoint(ref _force, ref bodyAttachPoint);
        }

        #region ApplyForce variables

        private const float _epsilon = .00001f;
        private Vector2 _bodyVelocity = Vector2.Zero;
        private Vector2 _bodyWorldPoint = Vector2.Zero;
        private float _dampningForce;
        private Vector2 _differenceNormalized;
        private Vector2 _force;
        private float _springForce;
        private float _temp;
        //Note: Cleanup. Never used.
        //private Vector2 vectorTemp1 = Vector2.Zero;
        //private Vector2 vectorTemp2 = Vector2.Zero;

        #endregion
    }
}