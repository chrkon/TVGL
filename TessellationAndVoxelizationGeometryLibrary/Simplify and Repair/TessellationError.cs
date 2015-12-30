﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StarMathLib;

namespace TVGL
{
    public class TessellationError
    {
        private double _edgeFaceRatio = double.NaN;
        public List<Tuple<Edge, List<PolygonalFace>>> OverusedEdges { get; private set; }
        public List<Edge> SingledSidedEdges { get; private set; }
        public List<int[]> DegenerateFaces { get; private set; }
        public List<int[]> DuplicateFaces { get; private set; }
        public List<PolygonalFace> NonTriangularFaces { get; private set; }
        public List<PolygonalFace> FacesWithOneVertex { get; private set; }
        public List<PolygonalFace> FacesWithOneEdge { get; private set; }
        public List<PolygonalFace> FacesWithTwoVertices { get; private set; }
        public List<PolygonalFace> FacesWithTwoEdges { get; private set; }
        public List<PolygonalFace> FacesWithNegligibleArea { get; private set; }
        public List<Tuple<PolygonalFace, Edge>> EdgesThatDoNotLinkBackToFace { get; private set; }
        public List<Tuple<Vertex, Edge>> EdgesThatDoNotLinkBackToVertex { get; private set; }
        public List<Tuple<PolygonalFace, Vertex>> VertsThatDoNotLinkBackToFace { get; private set; }
        public List<Tuple<Edge, Vertex>> VertsThatDoNotLinkBackToEdge { get; private set; }
        public List<Tuple<Edge, PolygonalFace>> FacesThatDoNotLinkBackToEdge { get; private set; }
        public List<Tuple<Vertex, PolygonalFace>> FacesThatDoNotLinkBackToVertex { get; private set; }
        public List<Edge> EdgesWithBadAngle { get; private set; }

        public double EdgeFaceRatio
        {
            get { return _edgeFaceRatio; }
            private set { _edgeFaceRatio = value; }
        }

        #region Check Model Integrity
        /// <summary>
        /// Checks the model integrity.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="repairAutomatically">The repair automatically.</param>
        public static void CheckModelIntegrity(TessellatedSolid ts, bool repairAutomatically = true)
        {
            Debug.WriteLine("Model Integrity Check...");
            if (ts.MostPolygonSides > 3) storeHigherThanTriFaces(ts);
            var edgeFaceRatio = ts.NumberOfEdges / (double)ts.NumberOfFaces;
            if (ts.MostPolygonSides == 3 && !edgeFaceRatio.IsPracticallySame(1.5)) storeEdgeFaceRatio(ts, edgeFaceRatio);
            //Check if each face has cyclic references with each edge, vertex, and adjacent faces.
            var facesWithMissingAdjacency = new List<PolygonalFace>();
            foreach (var face in ts.Faces)
            {
                if (face.Vertices.Count == 1) storeFaceWithOneVertex(ts, face);
                if (face.Vertices.Count == 2) storeFaceWithTwoVertices(ts, face);
                if (face.Edges.Count == 1) storeFaceWithOneEdge(ts, face);
                if (face.Edges.Count == 2) storeFaceWithOTwoEdges(ts, face);
                if (face.Area.IsNegligible(ts.sameTolerance)) storeFaceWithNegligibleArea(ts, face);
                foreach (var edge in face.Edges)
                    if (edge.OwnedFace != face && edge.OtherFace != face) storeEdgeDoesNotLinkBackToFace(ts, face, edge);
                foreach (var vertex in face.Vertices)
                    if (!vertex.Faces.Contains(face)) storeVertexDoesNotLinkBackToFace(ts, face, vertex);
                facesWithMissingAdjacency.AddRange(from adjacentFace in face.AdjacentFaces where adjacentFace == null select face);
                //Try to repair missing adjacency 
                //foreach (var face1 in facesWithMissingAdjacency)
                //{
                //    foreach (var face2 in facesWithMissingAdjacency)
                //    {
                //        if (face1 == face2) continue;
                //        foreach (var edge in face1.Edges)
                //        {
                //            if (face2.Edges.Contains(edge))
                //        }
                //    }
                //}
            }
            //Check if each edge has cyclic references with each vertex and each face.
            var faceDoesNotLinkBackToEdge = 0;
            var vertDoesNotLinkBackToEdge = 0;
            var edgeBadAngle = 0;
            foreach (var edge in ts.Edges)
            {
                if (edge.EdgeReference != TessellatedSolid.SetEdgeChecksum(edge.From, edge.To)) throw new Exception();
                if (!edge.OwnedFace.Edges.Contains(edge)) storeFaceDoesNotLinkBackToEdge(ts, edge, edge.OwnedFace);
                if (!edge.OtherFace.Edges.Contains(edge)) storeFaceDoesNotLinkBackToEdge(ts, edge, edge.OtherFace);
                if (!edge.To.Edges.Contains(edge)) storeVertDoesNotLinkBackToEdge(ts, edge, edge.To);
                if (!edge.From.Edges.Contains(edge)) storeVertDoesNotLinkBackToEdge(ts, edge, edge.From);
                if (double.IsNaN(edge.InternalAngle) || edge.InternalAngle < 0 || edge.InternalAngle > 2*Math.PI)  
                    storeEdgeHasBadAngle(ts, edge);
            }
            //Check if each vertex has cyclic references with each edge and each face.
            foreach (var vertex in ts.Vertices)
            {
                foreach (var edge in vertex.Edges)
                    if (edge.To != vertex && edge.From != vertex) storeEdgeDoesNotLinkBackToVertex(ts, vertex, edge);
                foreach (var face in vertex.Faces)
                    if (!face.Vertices.Contains(vertex)) storeFaceDoesNotLinkBackToVertex(ts, vertex, face);
            }
            if (ts.Errors == null)
            {
                Debug.WriteLine("** Model contains no errors.");
                return;
            }
            if (repairAutomatically)
            {
                Debug.WriteLine("Some errors found. Attempting to Repair...");
                var success = ts.Errors.Repair(ts);
                if (success)
                {
                    ts.Errors = null;
                    Debug.WriteLine("Repair successfully fixed the model.");
                }
                else Debug.WriteLine("Repair did not successfully fix all the problems.");
                CheckModelIntegrity(ts, false);
                return;
            }
            ts.Errors.Report();
        }


        public void Report()
        {
            Debug.WriteLine("Errors found in model:");
            Debug.WriteLine("======================");
            if (NonTriangularFaces != null)
                Debug.WriteLine("==> {0} faces are polygons with more than 3 sides.", NonTriangularFaces.Count);
            if (!double.IsNaN(EdgeFaceRatio))
                Debug.WriteLine("==> Edges / Faces = {0}, but it should be 1.5.", EdgeFaceRatio);
            if (OverusedEdges != null)
            {
                Debug.WriteLine("==> {0} overused edges.", OverusedEdges.Count);
                Debug.WriteLine("    The number of faces per overused edge: " +
                                OverusedEdges.Select(p => p.Item2.Count).MakePrintString());
            }
            if (SingledSidedEdges != null) Debug.WriteLine("==> {0} single-sided edges.", SingledSidedEdges.Count);
            if (DegenerateFaces != null) Debug.WriteLine("==> {0} degenerate faces in file.", DegenerateFaces.Count);
            if (DuplicateFaces != null) Debug.WriteLine("==> {0} duplicate faces in file.", DuplicateFaces.Count);
            if (FacesWithOneVertex != null)
                Debug.WriteLine("==> {0} faces with only one vertex.", FacesWithOneVertex.Count);
            if (FacesWithOneEdge != null) Debug.WriteLine("==> {0} faces with only one edge.", FacesWithOneEdge.Count);
            if (FacesWithTwoVertices != null) Debug.WriteLine("==> {0}  faces with only two vertices.", FacesWithTwoVertices.Count);
            if (FacesWithTwoEdges != null) Debug.WriteLine("==> {0}  faces with only two edges.", FacesWithTwoEdges.Count);
            if (FacesWithNegligibleArea != null) Debug.WriteLine("==> {0}  faces with negligible.", FacesWithNegligibleArea.Count);
            if (EdgesWithBadAngle != null) Debug.WriteLine("==> {0} edges with bad angles.", EdgesWithBadAngle.Count);
            if (EdgesThatDoNotLinkBackToFace != null)
                Debug.WriteLine("==> {0} edges that do not link back to faces that link to them.",
                    EdgesThatDoNotLinkBackToFace.Count);
            if (EdgesThatDoNotLinkBackToVertex != null)
                Debug.WriteLine("==> {0} edges that do not link back to vertices that link to them.",
                    EdgesThatDoNotLinkBackToVertex.Count);
            if (VertsThatDoNotLinkBackToFace != null)
                Debug.WriteLine("==> {0} vertices that do not link back to faces that link to them.",
                    VertsThatDoNotLinkBackToFace.Count);
            if (VertsThatDoNotLinkBackToEdge != null)
                Debug.WriteLine("==> {0} vertices that do not link back to edges that link to them.",
                    VertsThatDoNotLinkBackToEdge.Count);
            if (FacesThatDoNotLinkBackToEdge != null)
                Debug.WriteLine("==> {0} faces that do not link back to edges that link to them.",
                    FacesThatDoNotLinkBackToEdge.Count);
            if (FacesThatDoNotLinkBackToVertex != null)
                Debug.WriteLine("==> {0} faces that do not link back to vertices that link to them.",
                    FacesThatDoNotLinkBackToVertex.Count);
        }


        #endregion
        #region Error Storing
        private static void storeHigherThanTriFaces(TessellatedSolid ts)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.NonTriangularFaces == null)
                ts.Errors.NonTriangularFaces = new List<PolygonalFace>();
            foreach (var face in ts.Faces)
                if (face.Vertices.Count > 3)
                    ts.Errors.NonTriangularFaces.Add(face);
        }
        private static void storeEdgeFaceRatio(TessellatedSolid ts, double edgeFaceRatio)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            ts.Errors.EdgeFaceRatio = edgeFaceRatio;
        }

        private static void storeFaceDoesNotLinkBackToVertex(TessellatedSolid ts, Vertex vertex, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesThatDoNotLinkBackToVertex == null)
                ts.Errors.FacesThatDoNotLinkBackToVertex
                    = new List<Tuple<Vertex, PolygonalFace>> { new Tuple<Vertex, PolygonalFace>(vertex, face) };
            else ts.Errors.FacesThatDoNotLinkBackToVertex.Add(new Tuple<Vertex, PolygonalFace>(vertex, face));
        }

        private static void storeEdgeDoesNotLinkBackToVertex(TessellatedSolid ts, Vertex vertex, Edge edge)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.EdgesThatDoNotLinkBackToVertex == null)
                ts.Errors.EdgesThatDoNotLinkBackToVertex
                    = new List<Tuple<Vertex, Edge>> { new Tuple<Vertex, Edge>(vertex, edge) };
            else ts.Errors.EdgesThatDoNotLinkBackToVertex.Add(new Tuple<Vertex, Edge>(vertex, edge));
        }

        private static void storeEdgeHasBadAngle(TessellatedSolid ts, Edge edge)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.EdgesWithBadAngle == null) ts.Errors.EdgesWithBadAngle = new List<Edge> { edge };
            else if (!ts.Errors.EdgesWithBadAngle.Contains(edge)) ts.Errors.EdgesWithBadAngle.Add(edge);
        }

        private static void storeVertDoesNotLinkBackToEdge(TessellatedSolid ts, Edge edge, Vertex vert)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.VertsThatDoNotLinkBackToEdge == null)
                ts.Errors.VertsThatDoNotLinkBackToEdge
                    = new List<Tuple<Edge, Vertex>> { new Tuple<Edge, Vertex>(edge, vert) };
            else ts.Errors.VertsThatDoNotLinkBackToEdge.Add(new Tuple<Edge, Vertex>(edge, vert));
        }

        private static void storeFaceDoesNotLinkBackToEdge(TessellatedSolid ts, Edge edge, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesThatDoNotLinkBackToEdge == null)
                ts.Errors.FacesThatDoNotLinkBackToEdge
                    = new List<Tuple<Edge, PolygonalFace>> { new Tuple<Edge, PolygonalFace>(edge, face) };
            else ts.Errors.FacesThatDoNotLinkBackToEdge.Add(new Tuple<Edge, PolygonalFace>(edge, face));
        }

        private static void storeVertexDoesNotLinkBackToFace(TessellatedSolid ts, PolygonalFace face, Vertex vertex)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.VertsThatDoNotLinkBackToFace == null)
                ts.Errors.VertsThatDoNotLinkBackToFace
                    = new List<Tuple<PolygonalFace, Vertex>> { new Tuple<PolygonalFace, Vertex>(face, vertex) };
            else ts.Errors.VertsThatDoNotLinkBackToFace.Add(new Tuple<PolygonalFace, Vertex>(face, vertex));
        }

        private static void storeEdgeDoesNotLinkBackToFace(TessellatedSolid ts, PolygonalFace face, Edge edge)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.EdgesThatDoNotLinkBackToFace == null)
                ts.Errors.EdgesThatDoNotLinkBackToFace
                    = new List<Tuple<PolygonalFace, Edge>> { new Tuple<PolygonalFace, Edge>(face, edge) };
            else ts.Errors.EdgesThatDoNotLinkBackToFace.Add(new Tuple<PolygonalFace, Edge>(face, edge));
        }

        private static void storeFaceWithNegligibleArea(TessellatedSolid ts, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesWithNegligibleArea == null) ts.Errors.FacesWithNegligibleArea = new List<PolygonalFace> { face };
            else ts.Errors.FacesWithNegligibleArea.Add(face);
        }

        private static void storeFaceWithOTwoEdges(TessellatedSolid ts, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesWithTwoEdges == null) ts.Errors.FacesWithTwoEdges = new List<PolygonalFace> { face };
            else ts.Errors.FacesWithTwoEdges.Add(face);
        }

        private static void storeFaceWithTwoVertices(TessellatedSolid ts, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesWithTwoVertices == null) ts.Errors.FacesWithTwoVertices = new List<PolygonalFace> { face };
            else ts.Errors.FacesWithTwoVertices.Add(face);
        }

        private static void storeFaceWithOneEdge(TessellatedSolid ts, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesWithOneEdge == null) ts.Errors.FacesWithOneEdge = new List<PolygonalFace> { face };
            else ts.Errors.FacesWithOneEdge.Add(face);
        }

        private static void storeFaceWithOneVertex(TessellatedSolid ts, PolygonalFace face)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.FacesWithOneVertex == null) ts.Errors.FacesWithOneVertex = new List<PolygonalFace> { face };
            else ts.Errors.FacesWithOneVertex.Add(face);
        }

        internal static void StoreOverusedEdges(TessellatedSolid ts, IEnumerable<Tuple<Edge, List<PolygonalFace>>> edgeFaceTuples)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            ts.Errors.OverusedEdges = edgeFaceTuples.ToList();
        }
        internal static void StoreSingleSidedEdges(TessellatedSolid ts, IEnumerable<Edge> singledSidedEdges)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            ts.Errors.SingledSidedEdges = singledSidedEdges.ToList();
        }


        internal static void StoreDegenerateFace(TessellatedSolid ts, int[] faceVertexIndices)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.DegenerateFaces == null) ts.Errors.DegenerateFaces = new List<int[]> { faceVertexIndices };
            else ts.Errors.DegenerateFaces.Add(faceVertexIndices);
        }

        internal static void StoreDuplicateFace(TessellatedSolid ts, int[] faceVertexIndices)
        {
            if (ts.Errors == null) ts.Errors = new TessellationError();
            if (ts.Errors.DuplicateFaces == null) ts.Errors.DuplicateFaces = new List<int[]> { faceVertexIndices };
            else ts.Errors.DuplicateFaces.Add(faceVertexIndices);
        }
        #endregion

        #region Repair Functions

        internal bool Repair(TessellatedSolid ts)
        {
            var completelyRepaired = true;
            if (EdgesWithBadAngle != null)
                completelyRepaired = completelyRepaired && FlipFacesBasedOnBadAngles(ts);
            if (NonTriangularFaces != null)
                completelyRepaired = completelyRepaired && DivideUpNonTriangularFaces(ts);
            if (SingledSidedEdges != null) //what about faces with only one or two edges?
                completelyRepaired = completelyRepaired && RepairMissingFacesFromEdges(ts);
            if (FacesWithNegligibleArea != null)
                completelyRepaired = completelyRepaired && RepairNeglibleAreaFaces(ts);
            return completelyRepaired;
        }

        private bool FlipFacesBasedOnBadAngles(TessellatedSolid ts)
        {
            var edgesWithBadAngles = new HashSet<Edge>(ts.Errors.EdgesWithBadAngle);
            var facesToConsider = new HashSet<PolygonalFace>(
                edgesWithBadAngles.SelectMany(e => new[] { e.OwnedFace, e.OtherFace }).Distinct());
            var allEdgesToUpdate = new HashSet<Edge>();
            foreach (var face in facesToConsider)
            {
                var edgesToUpdate = new List<Edge>();
                foreach (var edge in face.Edges)
                {
                    if (edgesWithBadAngles.Contains(edge)) edgesToUpdate.Add(edge);
                    else if (facesToConsider.Contains((edge.OwnedFace == face) ? edge.OtherFace : edge.OwnedFace))
                        edgesToUpdate.Add(edge);
                    else break;
                }
                if (edgesToUpdate.Count < face.Edges.Count) continue;
                face.Normal = face.Normal.multiply(-1);
                face.Edges.Reverse();
                face.Vertices.Reverse();
                foreach (var edge in edgesToUpdate)
                    if (!allEdgesToUpdate.Contains(edge)) allEdgesToUpdate.Add(edge);
            }
            foreach (var edge in allEdgesToUpdate)
            {
                edge.Update();
                ts.Errors.EdgesWithBadAngle.Remove(edge);
            }
            if (ts.Errors.EdgesWithBadAngle.Any()) return false;
            ts.Errors.EdgesWithBadAngle = null;
            return true;
        }


        private bool DivideUpNonTriangularFaces(TessellatedSolid ts)
        {
            var allNewFaces = new List<PolygonalFace>();
            var singleSidedEdges = new HashSet<Edge>();
            foreach (var nonTriangularFace in ts.Errors.NonTriangularFaces)
            {
                var newFaces = new List<PolygonalFace>();
                foreach (var edge in nonTriangularFace.Edges)
                {
                    if (!singleSidedEdges.Contains(edge)) singleSidedEdges.Add(edge);
                }
                var triangles = TriangulatePolygon.Run(new List<List<Vertex>> { nonTriangularFace.Vertices }, nonTriangularFace.Normal);
                foreach (var triangle in triangles)
                {
                    var newFace = new PolygonalFace(triangle, nonTriangularFace.Normal) { color = nonTriangularFace.color };
                    newFaces.Add(newFace);
                }
                ts.AddPrimitive(new Flat(newFaces));
                allNewFaces.AddRange(newFaces);
            }
            ts.RemoveFaces(ts.Errors.NonTriangularFaces);
            ts.Errors.NonTriangularFaces = null;
            ts.MostPolygonSides = 3;
            ts.Errors.SingledSidedEdges = singleSidedEdges.ToList();
            return LinkUpNewFaces(allNewFaces, ts);
        }

        private bool RepairMissingFacesFromEdges(TessellatedSolid ts)
        {
            var newFaces = new List<PolygonalFace>();
            var loops = new List<List<Vertex>>();
            var loopNormals = new List<double[]>();
            var attempts = 0;
            var remainingEdges = new List<Edge>(ts.Errors.SingledSidedEdges);
            while (remainingEdges.Count > 0 && attempts < remainingEdges.Count)
            {
                var loop = new List<Vertex>();
                var successful = true;
                var removedEdges = new List<Edge>();
                var remainingEdge = remainingEdges[0];
                var startVertex = remainingEdge.From;
                var newStartVertex = remainingEdge.To;
                var normal = remainingEdge.OwnedFace.Normal;
                loop.Add(newStartVertex);
                removedEdges.Add(remainingEdge);
                remainingEdges.RemoveAt(0);
                do
                {
                    var possibleNextEdges =
                        remainingEdges.Where(e => e.To == newStartVertex || e.From == newStartVertex).ToList();
                    if (possibleNextEdges.Count() != 1) successful = false;
                    else
                    {
                        var currentEdge = possibleNextEdges[0];
                        normal = normal.multiply(loop.Count).add(currentEdge.OwnedFace.Normal).divide(loop.Count + 1);
                        normal.normalizeInPlace();
                        newStartVertex = currentEdge.OtherVertex(newStartVertex);
                        loop.Add(newStartVertex);
                        removedEdges.Add(currentEdge);
                        remainingEdges.Remove(currentEdge);
                    }
                } while (newStartVertex != startVertex && successful);
                if (successful)
                {
                    //Average the normals from all the owned faces.
                    loopNormals.Add(normal);
                    loops.Add(loop);
                    attempts = 0;
                }
                else
                {
                    remainingEdges.AddRange(removedEdges);
                    attempts++;
                }
            }

            for (var i = 0; i < loops.Count; i++)
            {
                //if a simple triangle, create a new face from vertices
                if (loops[i].Count == 3)
                {
                    var newFace = new PolygonalFace(loops[i], loopNormals[i]);
                    newFaces.Add(newFace);
                }
                //Else, use the triangulate function
                else if (loops[i].Count > 3)
                {
                    //First, get an average normal from all vertices, assuming CCW order.
                    var triangles = TriangulatePolygon.Run(new List<List<Vertex>> { loops[i] }, loopNormals[i]);
                    foreach (var triangle in triangles)
                    {
                        var newFace = new PolygonalFace(triangle, loopNormals[i]);
                        newFaces.Add(newFace);
                    }
                }
            }
            if (newFaces.Count == 1) Debug.WriteLine("1 missing face was fixed");
            if (newFaces.Count > 1) Debug.WriteLine(newFaces.Count + " missing faces were fixed");
            return LinkUpNewFaces(newFaces, ts);
        }

        private bool LinkUpNewFaces(List<PolygonalFace> newFaces, TessellatedSolid ts)
        {
            ts.AddFaces(newFaces);
            //var completedEdges = new List<Edge>();
            var newEdges = new List<Edge>();
            var partlyDefinedEdges = SingledSidedEdges.ToDictionary(ts.SetEdgeChecksum);
            ts.UpdateAllEdgeCheckSums();

            foreach (var face in newFaces)
            {
                for (var j = 0; j < 3; j++)
                {
                    var fromVertex = face.Vertices[j];
                    var toVertex = face.Vertices[(j == 2) ? 0 : j + 1];
                    var checksum =TessellatedSolid.SetEdgeChecksum(fromVertex, toVertex);

                    if (partlyDefinedEdges.ContainsKey(checksum))
                    {
                        //Finish creating edge.
                        var edge = partlyDefinedEdges[checksum];
                        if (edge.OwnedFace == null) edge.OwnedFace = face;
                        else if (edge.OtherFace == null) edge.OtherFace = face;
                        face.Edges.Add(edge);
                        //completedEdges.Add(edge);
                        //partlyDefinedEdges.Remove(checksum);
                    }
                    else
                    {
                        var edge = new Edge(fromVertex, toVertex, face, null, true, checksum);
                        newEdges.Add(edge);
                        partlyDefinedEdges.Add(checksum, edge);
                    }
                }
            }
            ts.AddEdges(newEdges);
            foreach (var edge in partlyDefinedEdges.Values)
                ts.Errors.SingledSidedEdges.Remove(edge);
            if (!ts.Errors.SingledSidedEdges.Any()) ts.Errors.SingledSidedEdges = null;
            return true;
        }
        //This function repairs all the negligible area faces in the solid. 
        //For each negligible triangle, the longest edge and smallest edge are found.
        //The triangle is then collapsed to the vertex that both those edges share.
        //Note that this removes 2 triangles, 3 edges, and 1 vertex from the model.
        //The new triangles, will either be flattened (if they had different normals)
        //OR, they will simply be merged. 
        private static bool RepairNeglibleAreaFaces(TessellatedSolid ts)
        {
            var index = 0;
            while (index < ts.Errors.FacesWithNegligibleArea.Count)
            {
                var negligibleFace = ts.Errors.FacesWithNegligibleArea[index];
                var splittingEdge = negligibleFace.Edges.OrderByDescending(item => item.Length).First();
                //Get the vertex opposite the splitting edge
                var otherVertex = negligibleFace.OtherVertex(splittingEdge);
                //Get the faces to be removed
                var removeTheseFaces = new List<PolygonalFace>(otherVertex.Faces);
                //Get the edges to be removed.
                var removeTheseEdges = new List<Edge>(otherVertex.Edges);

                //Use the same vertex which already exists, rather that creating a new one.
                //This method will simplify the model by 2 triangles.
                var newEdges = new List<Edge>();
                var newFaces = new List<PolygonalFace>();
                //Get the smallest edge of the negligible face.
                var negligibleEdge = negligibleFace.Edges.OrderBy(item => item.Length).First();
                var collapseToVertex = negligibleEdge.To == otherVertex ? negligibleEdge.From : negligibleEdge.To;


                //Get the outer edges and faces
                var allAffectedEdges = new List<Edge>();
                var allAffectedFaces = new List<PolygonalFace>();
                foreach (var face in removeTheseFaces)
                {
                    if (!ts.Faces.Contains(face)) throw new Exception();
                    foreach (var edge in face.Edges.Where(edge => !removeTheseEdges.Contains(edge)))
                    {
                        allAffectedEdges.Add(edge); //Add this outer edge
                        allAffectedFaces.Add(edge.OwnedFace == face ? edge.OtherFace : edge.OwnedFace);
                    }
                }

                //Find the second face to be collapsed and the third vertices from the two faces to be collapsed
                //We don't want the negligible face.
                PolygonalFace secondFace = null;
                foreach (var face in removeTheseFaces.Where(face => face != negligibleFace))
                {
                    if (!face.Vertices.Contains(collapseToVertex)) continue;
                    //If it already contains the new vertex, then it must be the secondFace we are going to collapse
                    if (secondFace != null) goto errorWithFace; // new Exception("this condition can only happen once");
                    secondFace = face;
                }
                if (secondFace == null) goto errorWithFace; // new Exception("this face must be set");
                var thirdVertexOfSecondFace = negligibleFace.OtherVertex(negligibleEdge);
                var thirdVertexOfNegligibleFace = secondFace.OtherVertex(negligibleEdge);

                //Create new faces to replace the ones being removed.
                foreach (var face in removeTheseFaces)
                {
                    if (face == negligibleFace || face == secondFace)
                        continue; //We don't need to do anything with these faces for now.
                                  //Replace the vertices from this face. Keep the normal, or add guess bool. normalIsGuess.
                    var newFaceVertexList = new List<Vertex>(face.Vertices);
                    newFaceVertexList.Remove(otherVertex);
                    newFaceVertexList.Add(collapseToVertex);
                    var newFace = new PolygonalFace(newFaceVertexList, face.Normal);
                    newFaces.Add(newFace);
                }
                allAffectedFaces.AddRange(newFaces);

                //Create new edges to replace the ones being removed. 
                foreach (var edge in removeTheseEdges.Where(edge => edge != negligibleEdge))
                {
                    if ((edge.To == otherVertex && edge.From == thirdVertexOfNegligibleFace ||
                         edge.From == thirdVertexOfSecondFace))
                        continue; //We don't need this edge
                    if ((edge.From == otherVertex && edge.To == thirdVertexOfNegligibleFace ||
                         edge.To == thirdVertexOfSecondFace))
                        continue; //We don't need this edge
                    newEdges.Add(otherVertex == edge.From
                        ? new Edge(collapseToVertex, edge.OtherVertex(otherVertex), true)
                        : new Edge(edge.OtherVertex(otherVertex), collapseToVertex, true));
                }
                allAffectedEdges.AddRange(newEdges);

                //Set the owned and other faces of the affected edges. All new edges's faces are in newFaces
                foreach (var edge in allAffectedEdges)
                {
                    //Reset the owned and other.
                    edge.OwnedFace = null;
                    edge.OtherFace = null;
                    foreach (var face in allAffectedFaces.Where(face => face.Vertices.Contains(edge.To) && face.Vertices.Contains(edge.From)))
                    {
                        if(!face.Edges.Contains(edge)) face.Edges.Add(edge); //Update the edge list for this face
                        if (edge.OwnedFace == null) edge.OwnedFace = face;
                        else if (edge.OtherFace == null) edge.OtherFace = face;
                        else if (edge.OwnedFace == edge.OtherFace)
                            edge.OtherFace = face; //A mistake was made. Fix it.
                        else throw new Exception();
                    }
                }

                //Brief check to make sure everything was set properly
                foreach (var edge in allAffectedEdges)
                {
                    if (edge.OwnedFace == null || edge.OtherFace == null) goto errorWithFace;
                    if (edge.OwnedFace == edge.OtherFace) goto errorWithFace;
                }
                foreach (var face in newFaces.Where(face => face.Edges.Count() != 3 || face.Vertices.Count() != 3))
                {
                    goto errorWithFace;
                }

                //Remove and then add all the new faces and edges.
                //The remove functions also remove any circular reference back to the face or edge.
                ts.RemoveFaces(removeTheseFaces);
                ts.RemoveEdges(removeTheseEdges);
                ts.AddEdges(newEdges);
                ts.AddFaces(newFaces);

                //Check the normals of the new faces.
                foreach (var face in newFaces)
                {
                    foreach (var adjacentFace in face.AdjacentFaces)
                    {
                        if (!adjacentFace.AdjacentFaces.Contains(face)) goto errorWithFace;
                        if (face.Normal.dotProduct(adjacentFace.Normal).IsPracticallySame(-1.0)) goto errorWithFace;
                    }
                }

                //lastly, remove the unused vertex and re-update all the edge and face references,
                //since we removed a vertex at the start of this function.
                ts.RemoveVertex(otherVertex, true);
                ts.Errors.FacesWithNegligibleArea.RemoveAt(index);
                //Check if any of the other negligible area faces were part of this repair. If so, remove them from the error list.
                foreach (var face in allAffectedFaces.Where(face => ts.Errors.FacesWithNegligibleArea.Contains(face)))
                {
                    ts.Errors.FacesWithNegligibleArea.Remove(face);
                    if(face.Area.IsNegligible()) ts.Errors.FacesWithNegligibleArea.Add(face);
                }
                foreach (var face in removeTheseFaces.Where(face => ts.Errors.FacesWithNegligibleArea.Contains(face)))
                {
                    ts.Errors.FacesWithNegligibleArea.Remove(face);
                }
                foreach (var face in newFaces.Where(face => face.Area.IsNegligible()))
                {
                    ts.Errors.FacesWithNegligibleArea.Add(face);
                }
                
                continue;
                errorWithFace:
                index++;
            }
            if (index <= 0) return true;
            Debug.WriteLine("{0} negligible area faces remain unfixable.", index);
            return false;
        }

        #endregion
    }
}
