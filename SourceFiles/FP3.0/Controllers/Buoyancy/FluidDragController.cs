using System;
using System.Collections.Generic;
using FarseerPhysics.Collision;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Controllers.Buoyancy
{
    /// <summary>
    /// FluidDragController applies fluid physics to the bodies within it.  Things like fluid drag and fluid density
    /// can be adjusted to give semi-realistic motion for bodies in fluid.
    /// 
    /// The FluidDragController does nothing to define or control the MOTION of the fluid itself. It simply knows
    /// how to apply fluid forces to the bodies it contains.
    /// 
    /// In order for the FluidDragController to know when to apply forces and when not to apply forces, it needs to know
    /// when a body enters it.  This is done by supplying the FluidDragController with an <see cref="IFluidContainer"/> object.
    /// 
    /// <see cref="IFluidContainer"/> has two simple methods that need to be implemented. Intersect(AABB aabb), returns true if a given
    /// AABB object intersects it, false otherwise.  Contains(ref Vector2 vector) returns true if a given point is inside the 
    /// fluid container, false otherwise.
    /// 
    /// For a very simple example of a very simple fluid container. See the <see cref="AABBFluidContainer"/>.  This represents a fluid container
    /// in the shape of an AABB.
    /// 
    /// More complex fluid containers are where things get interesting.  The <see cref="WaveContainer"/> object is an example of a complex
    /// fluid container.  The <see cref="WaveContainer"/> simulates wave motion. It's driven by an algorithm (not physics) which dynamically 
    /// alters a polygonal shape to mimic waves.  Where it gets interesting is the <see cref="WaveContainer"/> also implements <see cref="IFluidContainer"/>. This allows 
    /// it to be used in conjunction with the FluidDragController.  Anything that falls into the dynamically changing fluid container
    /// defined by the <see cref="WaveContainer"/> will have fluid physics applied to it.
    /// 
    /// </summary>
    public sealed class FluidDragController : Controller
    {
        #region Delegates

        public delegate void EntryEventHandler(Fixture geom, Vertices verts);

        #endregion

        public EntryEventHandler Entry;

        private float _area;
        private Vector2 _axis = Vector2.Zero;
        private Vector2 _buoyancyForce = Vector2.Zero;
        private Vector2 _centroid = Vector2.Zero;
        private Vector2 _centroidVelocity;

        private float _dragArea;
        private IFluidContainer _fluidContainer;
        private Dictionary<Fixture, bool> _geomInFluidList;
        private List<Fixture> _geomList;
        private Vector2 _gravity = Vector2.Zero;
        private Vector2 _linearDragForce = Vector2.Zero;
        private float _max;
        private float _min;
        private float _partialMass;
        private float _rotationalDragTorque;
        private float _totalArea;
        private Vector2 _totalForce;
        private Vector2 _vert;
        private Vertices _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="FluidDragController"/> class.
        /// </summary>
        /// <param name="fluidContainer">An object that implements <see cref="IFluidContainer"/></param>
        /// <param name="density">Density of the fluid</param>
        /// <param name="linearDragCoefficient">Linear drag coefficient of the fluid</param>
        /// <param name="rotationalDragCoefficient">Rotational drag coefficient of the fluid</param>
        /// <param name="gravity">The direction gravity acts. Buoyancy force will act in opposite direction of gravity.</param>
        public FluidDragController(IFluidContainer fluidContainer, float density, float linearDragCoefficient,
                                   float rotationalDragCoefficient, Vector2 gravity)
        {
            _geomList = new List<Fixture>();
            _geomInFluidList = new Dictionary<Fixture, bool>();

            _fluidContainer = fluidContainer;
            Density = density;
            LinearDragCoefficient = linearDragCoefficient;
            AngularDragCoefficient = rotationalDragCoefficient;
            _gravity = gravity;
            _vertices = new Vertices();
        }

        /// <summary>
        /// Density of the fluid.  Higher values will make things more buoyant, lower values will cause things to sink.
        /// </summary>
        public float Density { get; set; }

        /// <summary>
        /// Controls the linear drag that the fluid exerts on the bodies within it.  Use higher values will simulate thick fluid, like honey, lower values to
        /// simulate water-like fluids.
        /// </summary>
        public float LinearDragCoefficient { get; set; }

        /// <summary>
        /// Controls the rotational drag that the fluid exerts on the bodies within it. Use higher values will simulate thick fluid, like honey, lower values to
        /// simulate water-like fluids. 
        /// </summary>
        public float AngularDragCoefficient { get; set; }

        /// <summary>
        /// Add a geom to be controlled by the fluid drag controller.  The geom does not need to already be in
        /// the fluid to add it to the controller. By calling this method you are telling the fluid drag controller
        /// to watch this geom and it if enters my fluid container, apply the fluid physics.
        /// </summary>
        /// <param name="geom">The geom to be added.</param>
        public void AddGeom(Fixture geom)
        {
            _geomList.Add(geom);
            _geomInFluidList.Add(geom, false);
        }

        /// <summary>
        /// Removes a geometry from the fluid drag controller.
        /// </summary>
        /// <param name="geom">The geom.</param>
        public void RemoveGeom(Fixture geom)
        {
            _geomList.Remove(geom);
            _geomInFluidList.Remove(geom);
        }

        /// <summary>
        /// Resets the fluid drag controller
        /// </summary>
        public void Reset()
        {
            _geomInFluidList.Clear();
            for (int i = 0; i < _geomList.Count; i++)
            {
                _geomInFluidList.Add(_geomList[i], false);
            }
        }

        public override void Update(float dt)
        {
            _fluidContainer.Update(dt);

            for (int i = 0; i < _geomList.Count; i++)
            {
                Fixture fixture = _geomList[i];
                Body body = fixture.Body;
                Vertices localVertices = fixture.Shape.GetVertices();
                _totalArea = fixture.Shape.Area;

                //If the AABB of the geometry does not intersect the fluidcontainer
                //continue to the next geometry
                AABB aabb;
                fixture.Shape.ComputeAABB(out aabb, ref fixture.Body._xf, 0);

                if (!_fluidContainer.Intersect(ref aabb))
                    continue;

                //Find the vertices contained in the fluidcontainer
                _vertices.Clear();

                for (int k = 0; k < localVertices.Count; k++)
                {
                    _vert = fixture.Body.GetWorldPoint(localVertices[k]);
                    if (_fluidContainer.Contains(ref _vert))
                    {
                        _vertices.Add(_vert);
                    }
                }

                //The geometry is not in the fluid, up til a certain point.
                if (_vertices.Count < localVertices.Count * 0.15f)
                    _geomInFluidList[fixture] = false;

                _area = _vertices.GetArea();

                if (_area < .0001)
                    continue;

                _centroid = _vertices.GetCentroid();

                //Calculate buoyancy force
                _buoyancyForce = -_gravity * (_area * fixture.Shape.Density) * Density;

                //Calculate linear and rotational drag
                _centroidVelocity = fixture.Body.GetLinearVelocityFromWorldPoint(_centroid);

                _axis.X = -_centroidVelocity.Y;
                _axis.Y = _centroidVelocity.X;

                //can't normalize a zero length vector
                if (_axis.X != 0 || _axis.Y != 0)
                    _axis.Normalize();

                _vertices.ProjectToAxis(ref _axis, out _min, out _max);
                _dragArea = Math.Abs(_max - _min);
                _partialMass = fixture.Body.Mass * (_area / _totalArea);
                _linearDragForce = -.5f * Density * _dragArea * LinearDragCoefficient * _partialMass * _centroidVelocity;
                _rotationalDragTorque = -fixture.Body.AngularVelocity * AngularDragCoefficient * _partialMass;

                //Add the buoyancy force and lienar drag force
                Vector2.Add(ref _buoyancyForce, ref _linearDragForce, out _totalForce);

                //Apply total force to the body
                body.ApplyForce(ref _totalForce);

                //Apply rotational drag
                body.ApplyTorque(_rotationalDragTorque);

                if (_geomInFluidList[_geomList[i]] == false)
                {
                    //The geometry is now in the water. Fire the Entry event
                    _geomInFluidList[_geomList[i]] = true;
                    if (Entry != null)
                    {
                        Entry(_geomList[i], _vertices);
                    }
                }
            }
        }
    }
}