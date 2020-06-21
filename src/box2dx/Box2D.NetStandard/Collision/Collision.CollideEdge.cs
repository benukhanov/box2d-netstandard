﻿/*
  Box2D.NetStandard Copyright © 2020 Ben Ukhanov & Hugh Phoenix-Hulme https://github.com/benzuk/box2d-netstandard
  Box2DX Copyright (c) 2009 Ihar Kalasouski http://code.google.com/p/box2dx
  
// MIT License

// Copyright (c) 2019 Erin Catto

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
*/

using System;
using System.Diagnostics;
using System.Numerics;
using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Common;
using Math = Box2D.NetStandard.Common.Math;

namespace Box2D.NetStandard.Collision {
  public partial class Collision {
    // This implements 2-sided edge vs circle collision.
    internal static void CollideEdgeAndCircle(out Manifold    manifold, in EdgeShape edgeA, in Transform xfA,
      in                                        CircleShape circleB,  in Transform xfB) {
      manifold = new Manifold();

      manifold.pointCount = 0;

      Vector2 Q = Math.MulT(xfA, Math.Mul(xfB, circleB.m_p));

      Vector2 A = edgeA.m_vertex1, B = edgeA.m_vertex2;
      Vector2 e = B - A;

      float u = Vector2.Dot(e, B - Q);
      float v = Vector2.Dot(e, Q - A);

      float radius = edgeA.m_radius + circleB.m_radius;

      ContactFeature cf = new ContactFeature();
      cf.indexB = 0;
      cf.typeB  = (byte) ContactFeatureType.Vertex;
      

      // Region A
      if (v <= 0.0f) {
        Vector2 P  = A;
        Vector2 d  = Q - P;
        float  dd = Vector2.Dot(d, d);
        if (dd > radius * radius) {
          return;
        }

        // Is there an edge connected to A?
        if (edgeA.m_vertex0.HasValue) {
          Vector2 A1 = edgeA.m_vertex0.Value;
          Vector2 B1 = A;
          Vector2 e1 = B1 - A1;
          float  u1 = Vector2.Dot(e1, B1 - Q);

          // Is the circle in Region AB of the previous edge?
          if (u1 > 0.0f) {
            return;
          }
        }

        cf.indexA                     = 0;
        cf.typeA                      = (byte) ContactFeatureType.Vertex;
        manifold.pointCount           = 1;
        manifold.type                 = ManifoldType.Circles;
        manifold.localNormal          = Vector2.Zero;
        manifold.localPoint           = P;
        manifold.points[0] = new ManifoldPoint();
        manifold.points[0].id.key     = 0;
        manifold.points[0].id.cf      = cf;
        manifold.points[0].localPoint = circleB.m_p;
        return;
      }

      // Region B
      if (u <= 0.0f) {
        Vector2 P  = B;
        Vector2 d  = Q - P;
        float  dd = Vector2.Dot(d, d);
        if (dd > radius * radius) {
          return;
        }

        // Is there an edge connected to B?
        if (edgeA.m_vertex3.HasValue) {
          Vector2 B2 = edgeA.m_vertex3.Value;
          Vector2 A2 = B;
          Vector2 e2 = B2 - A2;
          float  v2 = Vector2.Dot(e2, Q - A2);

          // Is the circle in Region AB of the next edge?
          if (v2 > 0.0f) {
            return;
          }
        }

        cf.indexA                     = 1;
        cf.typeA                      = (byte) ContactFeatureType.Vertex;
        manifold.pointCount           = 1;
        manifold.type                 = ManifoldType.Circles;
        manifold.localNormal          = Vector2.Zero;
        manifold.localPoint           = P;
        manifold.points[0] = new ManifoldPoint();
        manifold.points[0].id.key     = 0;
        manifold.points[0].id.cf      = cf;
        manifold.points[0].localPoint = circleB.m_p;
        return;
      }

      {
        // Region AB
        float den = Vector2.Dot(e, e);
        Debug.Assert(den > 0.0f);
        Vector2 P  = (1.0f / den) * (u * A + v * B);
        Vector2 d  = Q - P;
        float  dd = Vector2.Dot(d, d);
        if (dd > radius * radius) {
          return;
        }

        Vector2 n = new Vector2(-e.Y, e.X);
        if (Vector2.Dot(n, Q - A) < 0.0f) {
          n = new Vector2(-n.X, -n.Y);
        }

        n = Vector2.Normalize(n);

        cf.indexA                     = 0;
        cf.typeA                      = (byte) ContactFeatureType.Face;
        manifold.pointCount           = 1;
        manifold.type                 = ManifoldType.FaceA;
        manifold.localNormal          = n;
        manifold.localPoint           = A;
        manifold.points[0] = new ManifoldPoint();
        manifold.points[0].id.key     = 0;
        manifold.points[0].id.cf      = cf;
        manifold.points[0].localPoint = circleB.m_p;
      }
    }

    internal static void CollideEdgeAndPolygon(out Manifold     manifold,
      in                           EdgeShape    edgeA,    in Transform xfA,
      in                           PolygonShape polygonB, in Transform xfB) {
      EPCollider collider = new EPCollider();
      collider.Collide(out manifold, edgeA, xfA, polygonB, xfB);
    }
  }

  // This structure is used to keep track of the best separating axis.
  struct EPAxis {
    internal enum AxisType {
      Unknown,
      EdgeA,
      EdgeB
    };

    internal AxisType type;
    internal int    index;
    internal float    separation;
  };

// This holds polygon B expressed in frame A.
  class TempPolygon {
    internal Vector2[] vertices = new Vector2[Settings.MaxPolygonVertices];
    internal Vector2[] normals  = new Vector2[Settings.MaxPolygonVertices];
    internal int    count;
  };

// Reference face used for clipping
  struct ReferenceFace {
    internal int i1;
    internal int i2;

    internal Vector2 v1;
    internal Vector2 v2;

    internal Vector2 normal;

    internal Vector2 sideNormal1;
    internal float  sideOffset1;

    internal Vector2 sideNormal2;
    internal float  sideOffset2;
  };

// This class collides and edge and a polygon, taking into account edge adjacency.
  struct EPCollider {
    // enum VertexType {
    //   e_isolated,
    //   e_concave,
    //   e_convex
    // };

    TempPolygon m_polygonB;

    Transform  m_xf;
    Vector2     m_centroidB;
    private Vector2? m_v0, m_v3;
    Vector2     m_v1,      m_v2;
    Vector2     m_normal0, m_normal1, m_normal2;
    Vector2     m_normal;
    // VertexType m_type1,      m_type2;
    Vector2     m_lowerLimit, m_upperLimit;
    float      m_radius;
    bool       m_front;


// Algorithm:
// 1. Classify v1 and v2
// 2. Classify polygon centroid as front or back
// 3. Flip normal if necessary
// 4. Initialize normal range to [-pi, pi] about face normal
// 5. Adjust normal range according to adjacent edges
// 6. Visit each separating axes, only accept axes within the range
// 7. Return if _any_ axis indicates separation
// 8. Clip
    internal void Collide(out Manifold     manifold, in EdgeShape edgeA, in Transform xfA,
      in                      PolygonShape polygonB, in Transform xfB) {
      m_xf        = Math.MulT(xfA, xfB);
      
      m_centroidB = Math.Mul(m_xf, polygonB.m_centroid);
      
      m_v0        = edgeA.m_vertex0;
      m_v1        = edgeA.m_vertex1;
      m_v2        = edgeA.m_vertex2;
      m_v3        = edgeA.m_vertex3;
      
      Vector2 edge1      = Vector2.Normalize(m_v2 - m_v1);
      m_normal1 = new Vector2(edge1.Y, -edge1.X);
      float offset1 = Vector2.Dot(m_normal1, m_centroidB - m_v1);
      float offset0 = 0.0f, offset2 = 0.0f;

      bool convex1 = false, convex2 = false;

      // Is there a preceding edge?
      if (m_v0.HasValue) {
        Vector2 edge0 = Vector2.Normalize(m_v1 - m_v0.Value);
        m_normal0 = new Vector2(edge0.Y, -edge0.X);
        convex1   = Vectex.Cross(edge0, edge1) >= 0.0f;
        offset0   = Vector2.Dot(m_normal0, m_centroidB - m_v0.Value);
      }

      // Is there a following edge?
      if (m_v3.HasValue) {
        Vector2 edge2 = Vector2.Normalize(m_v3.Value - m_v2);
        m_normal2 = new Vector2(edge2.Y, -edge2.X);
        convex2   = Vectex.Cross(edge1, edge2) > 0.0f;
        offset2   = Vector2.Dot(m_normal2, m_centroidB - m_v2);
      }

      // Determine front or back collision. Determine collision normal limits.
      if (m_v0.HasValue && m_v3.HasValue) {
        if (convex1 && convex2) {
          m_front = offset0 >= 0.0f || offset1 >= 0.0f || offset2 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal0;
            m_upperLimit = m_normal2;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal1;
            m_upperLimit = -m_normal1;
          }
        }
        else if (convex1) {
          m_front = offset0 >= 0.0f || (offset1 >= 0.0f && offset2 >= 0.0f);
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal0;
            m_upperLimit = m_normal1;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal2;
            m_upperLimit = -m_normal1;
          }
        }
        else if (convex2) {
          m_front = offset2 >= 0.0f || (offset0 >= 0.0f && offset1 >= 0.0f);
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal1;
            m_upperLimit = m_normal2;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal1;
            m_upperLimit = -m_normal0;
          }
        }
        else {
          m_front = offset0 >= 0.0f && offset1 >= 0.0f && offset2 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal1;
            m_upperLimit = m_normal1;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal2;
            m_upperLimit = -m_normal0;
          }
        }
      }
      else if (m_v0.HasValue) {
        if (convex1) {
          m_front = offset0 >= 0.0f || offset1 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal0;
            m_upperLimit = -m_normal1;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = m_normal1;
            m_upperLimit = -m_normal1;
          }
        }
        else {
          m_front = offset0 >= 0.0f && offset1 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = m_normal1;
            m_upperLimit = -m_normal1;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = m_normal1;
            m_upperLimit = -m_normal0;
          }
        }
      }

      else if (m_v3.HasValue) {
        if (convex2) {
          m_front = offset1 >= 0.0f || offset2 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = -m_normal1;
            m_upperLimit = m_normal2;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal1;
            m_upperLimit = m_normal1;
          }
        }
        else {
          m_front = offset1 >= 0.0f && offset2 >= 0.0f;
          if (m_front) {
            m_normal     = m_normal1;
            m_lowerLimit = -m_normal1;
            m_upperLimit = m_normal1;
          }
          else {
            m_normal     = -m_normal1;
            m_lowerLimit = -m_normal2;
            m_upperLimit = m_normal1;
          }
        }
      }

      else {
        m_front = offset1 >= 0.0f;
        if (m_front) {
          m_normal     = m_normal1;
          m_lowerLimit = -m_normal1;
          m_upperLimit = -m_normal1;
        }
        else {
          m_normal     = -m_normal1;
          m_lowerLimit = m_normal1;
          m_upperLimit = m_normal1;
        }
      }

      // Get polygonB in frameA
      m_polygonB = new TempPolygon();
      m_polygonB.count = polygonB.m_count;
      for (int i = 0; i < polygonB.m_count; ++i) {
        m_polygonB.vertices[i] = Math.Mul(m_xf,   polygonB.m_vertices[i]);
        m_polygonB.normals[i]  = Math.Mul(m_xf.q, polygonB.m_normals[i]);
      }

      m_radius            = polygonB.m_radius + edgeA.m_radius;
      manifold            = new Manifold();
      manifold.pointCount = 0;

      EPAxis edgeAxis = ComputeEdgeSeparation();

      // If no valid normal can be found than this edge should not collide.
      if (edgeAxis.type == EPAxis.AxisType.Unknown) {
        return;
      }

      if (edgeAxis.separation > m_radius) {
        return;
      }

      EPAxis polygonAxis = ComputePolygonSeparation();
      if (polygonAxis.type != EPAxis.AxisType.Unknown && polygonAxis.separation > m_radius) {
        return;
      }

      // Use hysteresis for jitter reduction.
      const float k_relativeTol = 0.98f;
      const float k_absoluteTol = 0.001f;

      EPAxis primaryAxis;
      if (polygonAxis.type == EPAxis.AxisType.Unknown) {
        primaryAxis = edgeAxis;
      }
      else if (polygonAxis.separation > k_relativeTol * edgeAxis.separation + k_absoluteTol) {
        primaryAxis = polygonAxis;
      }
      else {
        primaryAxis = edgeAxis;
      }

      ClipVertex[] ie = new ClipVertex[2];

      ReferenceFace rf;
      if (primaryAxis.type == EPAxis.AxisType.EdgeA) {
        manifold.type = ManifoldType.FaceA;

        // Search for the polygon normal that is most anti-parallel to the edge normal.
        int bestIndex = 0;
        float bestValue = Vector2.Dot(m_normal, m_polygonB.normals[0]);
        for (int i = 1; i < m_polygonB.count; ++i) {
          float value = Vector2.Dot(m_normal, m_polygonB.normals[i]);
          if (value < bestValue) {
            bestValue = value;
            bestIndex = i;
          }
        }

        int i1 = bestIndex;
        int i2 = i1 + 1 < m_polygonB.count ? i1 + 1 : 0;

        ie[0].v            = m_polygonB.vertices[i1];
        ie[0].id.cf.indexA = 0;
        ie[0].id.cf.indexB = (byte) (i1);
        ie[0].id.cf.typeA  = (byte) ContactFeatureType.Face;
        ie[0].id.cf.typeB  = (byte) ContactFeatureType.Vertex;

        ie[1].v            = m_polygonB.vertices[i2];
        ie[1].id.cf.indexA = 0;
        ie[1].id.cf.indexB = (byte) (i2);
        ie[1].id.cf.typeA  = (byte) ContactFeatureType.Face;
        ie[1].id.cf.typeB  = (byte) ContactFeatureType.Vertex;

        if (m_front) {
          rf.i1     = 0;
          rf.i2     = 1;
          rf.v1     = m_v1;
          rf.v2     = m_v2;
          rf.normal = m_normal1;
        }
        else {
          rf.i1     = 1;
          rf.i2     = 0;
          rf.v1     = m_v2;
          rf.v2     = m_v1;
          rf.normal = -m_normal1;
        }
      }
      else {
        manifold.type = ManifoldType.FaceB;

        ie[0].v            = m_v1;
        ie[0].id.cf.indexA = 0;
        ie[0].id.cf.indexB = (byte) (primaryAxis.index);
        ie[0].id.cf.typeA  = (byte) ContactFeatureType.Vertex;
        ie[0].id.cf.typeB  = (byte) ContactFeatureType.Face;

        ie[1].v            = m_v2;
        ie[1].id.cf.indexA = 0;
        ie[1].id.cf.indexB = (byte) (primaryAxis.index);
        ie[1].id.cf.typeA  = (byte) ContactFeatureType.Vertex;
        ie[1].id.cf.typeB  = (byte) ContactFeatureType.Face;

        rf.i1     = primaryAxis.index;
        rf.i2     = rf.i1 + 1 < m_polygonB.count ? rf.i1 + 1 : 0;
        rf.v1     = m_polygonB.vertices[rf.i1];
        rf.v2     = m_polygonB.vertices[rf.i2];
        rf.normal = m_polygonB.normals[rf.i1];
      }

      rf.sideNormal1 = new Vector2(rf.normal.Y, -rf.normal.X);
      rf.sideNormal2 = -rf.sideNormal1;
      rf.sideOffset1 = Vector2.Dot(rf.sideNormal1, rf.v1);
      rf.sideOffset2 = Vector2.Dot(rf.sideNormal2, rf.v2);

      // Clip incident edge against extruded edge1 side edges.
      int        np;

      // Clip to box side 1
      np = Collision.ClipSegmentToLine(out ClipVertex[] clipPoints1, ie, rf.sideNormal1, rf.sideOffset1, rf.i1);
      if (np < Settings.MaxManifoldPoints) {
        return;
      }

      // Clip to negative box side 1
      np = Collision.ClipSegmentToLine(out ClipVertex[] clipPoints2, clipPoints1, rf.sideNormal2, rf.sideOffset2, rf.i2);
      if (np < Settings.MaxManifoldPoints) {
        return;
      }

      // Now clipPoints2 contains the clipped points.
      if (primaryAxis.type == EPAxis.AxisType.EdgeA) {
        manifold.localNormal = rf.normal;
        manifold.localPoint  = rf.v1;
      }
      else {
        manifold.localNormal = polygonB.m_normals[rf.i1];
        manifold.localPoint  = polygonB.m_vertices[rf.i1];
      }

      int pointCount = 0;
      for (int i = 0; i < Settings.MaxManifoldPoints; ++i) {
        float separation = Vector2.Dot(rf.normal, clipPoints2[i].v - rf.v1);

        if (separation <= m_radius) {
          ManifoldPoint cp = new ManifoldPoint();

          if (primaryAxis.type == EPAxis.AxisType.EdgeA) {
            cp.localPoint = Math.MulT(m_xf, clipPoints2[i].v);
            cp.id         = clipPoints2[i].id;
          }
          else {
            cp.localPoint   = clipPoints2[i].v;
            cp.id.cf.typeA  = clipPoints2[i].id.cf.typeB;
            cp.id.cf.typeB  = clipPoints2[i].id.cf.typeA;
            cp.id.cf.indexA = clipPoints2[i].id.cf.indexB;
            cp.id.cf.indexB = clipPoints2[i].id.cf.indexA;
          }

          manifold.points[pointCount] = cp;
          ++pointCount;
        }
      }

      manifold.pointCount = pointCount;
    }

    EPAxis ComputeEdgeSeparation() {
      EPAxis axis;
      axis.type       = EPAxis.AxisType.EdgeA;
      axis.index      = m_front ? 0 : 1;
      axis.separation = float.MaxValue;
      for (int i = 0; i < m_polygonB.count; ++i) {
        float s = Vector2.Dot(m_normal, m_polygonB.vertices[i] - m_v1);
        if (s < axis.separation) {
          axis.separation = s;
        }
      }

      return axis;
    }

    EPAxis ComputePolygonSeparation() {
      EPAxis axis;
      axis.type       = EPAxis.AxisType.Unknown;
      axis.index      = -1;
      axis.separation = float.MinValue;
      Vector2 perp = new Vector2(-m_normal.Y, m_normal.X);
      for (int i = 0; i < m_polygonB.count; ++i) {
        Vector2 n = -m_polygonB.normals[i];

        float s1 = Vector2.Dot(n, m_polygonB.vertices[i] - m_v1);
        float s2 = Vector2.Dot(n, m_polygonB.vertices[i] - m_v2);
        float s  = MathF.Min(s1, s2);

        if (s > m_radius) {
          // No collision
          axis.type       = EPAxis.AxisType.EdgeB;
          axis.index      = i;
          axis.separation = s;
          return axis;
        }

        // Adjacency
        if (Vector2.Dot(n, perp) >= 0.0f) {
          if (Vector2.Dot(n - m_upperLimit, m_normal) < -Settings.AngularSlop) {
            continue;
          }
        }
        else {
          if (Vector2.Dot(n - m_lowerLimit, m_normal) < -Settings.AngularSlop) {
            continue;
          }
        }

        if (s > axis.separation) {
          axis.type       = EPAxis.AxisType.EdgeB;
          axis.index      = i;
          axis.separation = s;
        }
      }

      return axis;
    }
  }
  
  enum ContactFeatureType:byte
  {
    Vertex = 0,
    Face   = 1
  };
  
  /// The features that intersect to form the contact point
  /// This must be 4 bytes or less.
  struct ContactFeature
  {
    internal byte indexA;		///< Feature index on shapeA
    internal byte indexB;		///< Feature index on shapeB
    internal byte typeA;		///< The feature type on shapeA
    internal byte typeB; ///< The feature type on shapeB
  };
}