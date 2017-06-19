﻿// ***********************************************************************
// Assembly         : TessellationAndVoxelizationGeometryLibrary
// Author           : Design Engineering Lab
// Created          : 04-18-2016
//
// Last Modified By : Design Engineering Lab
// Last Modified On : 05-25-2016
// ***********************************************************************
// <copyright file="AreaDecomposition.cs" company="Design Engineering Lab">
//     Copyright ©  2014
// </copyright>
// <summary></summary>
// ***********************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ClipperLib;
using StarMathLib;
using TVGL;

namespace TVGL
{
    /// <summary>
    ///     Outputs cross sectional area along a given axis
    /// </summary>
    public static class AreaDecomposition
    {
        #region Standard Area Decomposition. Non-uniform.
        /// <summary>
        ///     Runs the specified ts.
        /// </summary>
        /// <param name="ts">The ts.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="stepSize">Size of the step.</param>
        /// <param name="minOffset">The minimum offset.</param>
        /// <param name="ignoreNegativeSpace">if set to <c>true</c> [ignore negative space].</param>
        /// <param name="convexHull2DDecompositon">if set to <c>true</c> [convex hull2 d decompositon].</param>
        /// <param name="boundingRectangleArea">if set to <c>true</c> [bounding rectangle area].</param>
        /// <returns>List&lt;System.Double[]&gt;.</returns>
        /// <exception cref="Exception">Pick one or the other. Can't do both at the same time</exception>
        public static List<double[]> Run(TessellatedSolid ts, double[] axis, double stepSize,
            double minOffset = double.NaN, bool ignoreNegativeSpace = false, bool convexHull2DDecompositon = false,
            bool boundingRectangleArea = false)
        {
            //individualFaceAreas = new List<List<double[]>>(); //Plot changes for the area of each flat that makes up a slice. (e.g. 2 positive loop areas)
            if (convexHull2DDecompositon && boundingRectangleArea)
                throw new Exception("Pick one or the other. Can't do both at the same time");

            var outputData = new List<double[]>();
            if (double.IsNaN(minOffset)) minOffset = Math.Sqrt(ts.SameTolerance);
            if (stepSize <= minOffset * 2)
            {
                //"step size must be at least 2x as large as the min offset");
                //Change it rather that throwing an exception
                stepSize = minOffset * 2 + ts.SameTolerance;
            }
            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Tuple<Vertex, double>> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { axis }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousDistanceAlongAxis = sortedVertices[0].Item2; //This value can be negative
            var previousVertexDistance = previousDistanceAlongAxis;
            foreach (var element in sortedVertices)
            {
                var vertex = element.Item1;
                var distanceAlongAxis = element.Item2; //This value can be negative
                var difference1 = distanceAlongAxis - previousDistanceAlongAxis;
                var difference2 = distanceAlongAxis - previousVertexDistance;
                if (difference2 > minOffset && difference1 > stepSize)
                {
                    //Determine cross sectional area for section right after previous vertex
                    var distance = previousVertexDistance + minOffset; //X value (distance along axis) 
                    var cuttingPlane = new Flat(distance, axis);
                    List<List<Edge>> outputEdgeLoops = null;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var area = 0.0;
                    if (convexHull2DDecompositon) area = ConvexHull2DArea(edgeListDictionary, cuttingPlane);
                    else if (boundingRectangleArea) area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                    else
                        area = CrossSectionalArea(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops,
                            ignoreNegativeSpace); //Y value (area)
                    outputData.Add(new[] { distance, area });

                    //If the difference is far enough, add another data point right before the current vertex
                    //Use the vertex loops provided from the first pass above
                    if (difference2 > 3 * minOffset)
                    {
                        var distance2 = distanceAlongAxis - minOffset; //X value (distance along axis) 
                        cuttingPlane = new Flat(distance2, axis);
                        if (convexHull2DDecompositon) area = ConvexHull2DArea(edgeListDictionary, cuttingPlane);
                        else if (boundingRectangleArea) area = BoundingRectangleArea(edgeListDictionary, cuttingPlane);
                        else
                        {
                            inputEdgeLoops = outputEdgeLoops;
                            area = CrossSectionalArea(edgeListDictionary, cuttingPlane, out outputEdgeLoops,
                                inputEdgeLoops, ignoreNegativeSpace); //Y value (area)
                        }
                        outputData.Add(new[] { distance2, area });
                    }

                    //Update the previous distance used to make a data point
                    previousDistanceAlongAxis = distanceAlongAxis;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = distanceAlongAxis;
            }
            return outputData;
        }
        #endregion

        #region Uniform Directional Decomposition
        /// <summary>
        /// Returns the decomposition data found from each slice of the decomposition. This data is used in other methods.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        public static List<DecompositionData> UniformDirectionalDecomposition(TessellatedSolid ts, double[] direction,
            double stepSize)
        {
            var outputData = new List<DecompositionData>();

            List<Vertex> bottomVertices, topVertices;
            var length = MinimumEnclosure.GetLengthAndExtremeVertices(direction, ts.Vertices,
                out bottomVertices, out topVertices);

            //Set step size to an even increment over the entire length of the solid
            stepSize = length / Math.Round(length / stepSize + 1);

            //make the minimum step size 1/10 of the length.
            if (length < 10 * stepSize)
            {
                stepSize = length / 10;
            }

            //Choose whichever min offset is smaller
            var minOffset = Math.Min(Math.Sqrt(ts.SameTolerance), stepSize / 1000);

            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Tuple<Vertex, double>> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices, out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var firstDistance = sortedVertices.First().Item2;
            var furthestDistance = sortedVertices.Last().Item2;
            var distanceAlongAxis = firstDistance;
            var currentVertexIndex = 0;
            var inputEdgeLoops = new List<List<Edge>>();

            while (distanceAlongAxis < furthestDistance - stepSize)
            {
                distanceAlongAxis += stepSize;

                //Update vertex/edge list up until distanceAlongAxis
                for (var i = currentVertexIndex; i < sortedVertices.Count; i++)
                {
                    //Update the current vertex index so that this vertex is not visited again
                    //unless it causes the break ( > distanceAlongAxis), then it will start the 
                    //the next iteration.
                    currentVertexIndex = i;
                    var element = sortedVertices[i];
                    var vertex = element.Item1;
                    var vertexDistanceAlong = element.Item2;
                    //If a vertex is too close to the current distance, move it forward by the min offset.
                    //Update the edge list with this vertex.
                    if (vertexDistanceAlong.IsPracticallySame(distanceAlongAxis, minOffset))
                    {
                        //Move the distance enough so that this vertex is now less than 
                        distanceAlongAxis = vertexDistanceAlong + minOffset * 1.1;
                        //if (vertexDistanceAlong.IsPracticallySame(distanceAlongAxis, minOffset))
                        //{
                        //    throw new Exception("Error in implementation. Need to move the distance further");
                        //}
                    }
                    //Else, Break after we get to a vertex that is further than the distance along axis
                    if (vertexDistanceAlong > distanceAlongAxis)
                    {
                        //consider this vertex again next iteration
                        break;
                    }

                    //Else, it is less than the distance along. Update the edge list
                    //Add the passed vertices to a list so that they can be removed from the sorted vertices

                    //Update the edge dictionary that is used to determine the 3D loops.
                    foreach (var edge in vertex.Edges)
                    {
                        //Reset the input edge loops since we have added an edge
                        inputEdgeLoops = new List<List<Edge>>();

                        //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                        //and the second removes it from the list.
                        if (edgeListDictionary.ContainsKey(edge.IndexInList))
                        {
                            edgeListDictionary.Remove(edge.IndexInList);
                        }
                        else
                        {
                            edgeListDictionary.Add(edge.IndexInList, edge);
                        }
                    }
                }

                //Check to make sure that the minor shifts in the distance in the for loop above 
                //Did not move the distance beyond the furthest distance
                if (distanceAlongAxis > furthestDistance || !edgeListDictionary.Any()) break;
                //Make the slice
                var counter = 0;
                var current3DLoops = new List<List<Vertex>>();
                var successfull = true;
                var cuttingPlane = new Flat(distanceAlongAxis, direction);
                do
                {
                    try
                    {
                        List<List<Edge>> outputEdgeLoops;
                        current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops,
                            inputEdgeLoops);

                        //Use the same output edge loops for outer while loop, since the edge list does not change.
                        //If there is an error, it will occur before this loop.
                        inputEdgeLoops = outputEdgeLoops;
                    }
                    catch
                    {
                        counter++;
                        distanceAlongAxis += minOffset;
                        successfull = false;
                    }
                } while (!successfull && counter < 4);


                if (successfull)
                {
                    double[,] backTransform;
                    //Get a list of 2D paths from the 3D loops
                    var currentPaths =
                        current3DLoops.Select(
                            cp =>
                                MiscFunctions.Get2DProjectionPointsReorderingIfNecessary(cp, direction,
                                    out backTransform));

                    //Get the area of this layer
                    var area = current3DLoops.Sum(p => MiscFunctions.AreaOf3DPolygon(p, direction));
                    if (area < 0)
                    {
                        //Rather than throwing an exception, just assume the polygons were the wrong direction      
                        Debug.WriteLine(
                            "Area for a cross section in UniformDirectionalDecomposition was negative. This means there was an issue with the polygon ordering");
                    }

                    //Add the data to the output
                    outputData.Add(new DecompositionData(currentPaths, distanceAlongAxis));
                }
                else
                {
                    Debug.WriteLine("Slice at this distance was unsuccessful, even with multiple minimum offsets.");
                }
            }

            //Add the first and last cross sections. 
            //Note, these may not be great fits if step size is large
            outputData.Insert(0, new DecompositionData(outputData.First().Paths, firstDistance));
            outputData.Add(new DecompositionData(outputData.Last().Paths, furthestDistance));

            return outputData;
        }
        #endregion

        #region Additive Volume
        /// <summary>
        /// Gets the additive volume given a list of decomposition data
        /// </summary>
        /// <param name="decompData"></param>
        /// <param name="additiveAccuracy"></param>
        /// <param name="outputData"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static double AdditiveVolume(List<DecompositionData> decompData, double additiveAccuracy, out List<DecompositionData> outputData)
        {
            outputData = new List<DecompositionData>();
            var previousPolygons = new List<List<Point>>();
            var previousDistance = 0.0;
            var previousArea = 0.0;
            var additiveVolume = 0.0;
            var i = 0;
            var n = decompData.Count;
            foreach (var data in decompData)
            {
                var currentPaths = data.Paths;
                //Offset the distance back by the additive accuracy. THis acts as a vertical offset
                var distance = data.DistanceAlongDirection - additiveAccuracy;
                //currentPaths = PolygonOperations.UnionEvenOdd(currentPaths);

                //Offset if the additive accuracy is significant
                var areaPriorToOffset = MiscFunctions.AreaOfPolygon(currentPaths);
                var offsetPaths = !additiveAccuracy.IsNegligible() ? PolygonOperations.OffsetSquare(currentPaths, additiveAccuracy) : new List<List<Point>>(currentPaths);
                var areaAfterOffset = MiscFunctions.AreaOfPolygon(offsetPaths);
                //Simplify the paths, but remove any that are eliminated (e.g. points are all very close together)
                var simpleOffset = offsetPaths.Select(PolygonOperations.SimplifyFuzzy).Where(simplePath => simplePath.Any()).ToList();
                var areaAfterSimplification = MiscFunctions.AreaOfPolygon(simpleOffset);
                if (areaPriorToOffset > areaAfterOffset) throw new Exception("Path is ordered incorrectly");
                if (!areaAfterOffset.IsPracticallySame(areaAfterSimplification, areaAfterOffset * .05)) throw new Exception("Simplify Fuzzy Alterned the Geometry more than 5% of the area");

                //Union this new set of polygons with the previous set.
                if (previousPolygons.Any()) //If not the first iteration
                {
                    previousPolygons = previousPolygons.Select(PolygonOperations.SimplifyFuzzy).Where(simplePath => simplePath.Any()).ToList();
                    try
                    {
                        currentPaths = new List<List<Point>>(PolygonOperations.Union(previousPolygons, simpleOffset));
                    }
                    catch
                    {
                        var testArea1 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                        var testArea2 = previousPolygons.Sum(p => MiscFunctions.AreaOfPolygon(p));
                        if (testArea1.IsPracticallySame(testArea2, 0.01))
                        {
                            currentPaths = simpleOffset;
                            //They are probably throwing an error because they are closely overlapping
                        }
                        else
                        {
                            ////Debug Mode
                            //var previousData = outputData.Last();
                            //outputData = new List<DecompositionData>() { previousData, new DecompositionData( currentPaths, distance )};
                            //return 0.0;

                            //Run mode: Use previous path
                            Debug.WriteLine("Union failed and not similar");
                            //
                            currentPaths = outputData.Last().Paths;
                        }
                    }
                }

                //Get the area of this layer
                var area = currentPaths.Sum(p => MiscFunctions.AreaOfPolygon(p));
                if (area < 0)
                {
                    //Rather than throwing an exception, just assume the polygons were the wrong direction      
                    area = -area;
                    Debug.WriteLine("Area for a polygon in the Additive Volume estimate was negative. This means there was an issue with the polygon ordering");
                }

                //This is the first iteration. Add it to the output data.
                if (i == 0)
                {
                    outputData.Add(new DecompositionData(simpleOffset, distance));
                    var area2 = simpleOffset.Sum(p => MiscFunctions.AreaOfPolygon(p));
                    if (area2 < 0)
                    {
                        //Rather than throwing an exception, just assume the polygons were the wrong direction      
                        area2 = -area2;
                        Debug.WriteLine("The first polygon in the Additive Volume estimate was negative. This means there was an issue with the polygon ordering");
                    }
                    additiveVolume += additiveAccuracy * area2;
                }

                //Add the volume from this iteration.
                else if (!previousDistance.IsNegligible())
                {
                    var deltaX = Math.Abs(distance - previousDistance);
                    if (area < previousArea * .99)
                    {
                        ////Debug Mode
                        var previousData = outputData.Last();
                        outputData = new List<DecompositionData>() { previousData, new DecompositionData(currentPaths, distance) };
                        return 0.0;

                        //Run Mode: use previous area
                        Debug.WriteLine("Error in your implementation. This should never occur");
                        area = previousArea;

                    }
                    additiveVolume += deltaX * previousArea;
                    outputData.Add(new DecompositionData(currentPaths, distance));
                }

                //This is the last iteration. Add it to the output data.
                if (i == n - 1)
                {
                    outputData.Add(new DecompositionData(currentPaths, distance + additiveAccuracy));
                    additiveVolume += additiveAccuracy * area;
                }

                previousPolygons = currentPaths;
                previousDistance = distance;
                previousArea = area;
                i++;
            }
            return additiveVolume;
        }
        #endregion

        #region Get Cross Section at a Given Distance
        /// <summary>
        /// Gets the Cross Section for a given distance
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="distance"></param>
        /// <returns></returns>
        public static List<List<Point>> GetCrossSectionAtGivenDistance(TessellatedSolid ts, double[] direction, double distance)
        {
            var crossSection = new List<List<Point>>();

            //First, sort the vertices along the given axis. Duplicate distances are not important.
            List<Tuple<Vertex, double>> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices.ToList(), out sortedVertices, out duplicateRanges);

            var edgeListDictionary = new Dictionary<int, Edge>();
            var previousVertexDistance = sortedVertices[0].Item2; //This value can be negative
            foreach (var element in sortedVertices)
            {
                var vertex = element.Item1;
                var currentVertexDistance = element.Item2; //This value can be negative

                if (currentVertexDistance.IsPracticallySame(distance, ts.SameTolerance) || currentVertexDistance > distance)
                {
                    //Determine cross sectional area for section as close to given distance as possitible (after previous vertex, but before current vertex)
                    //But not actually on the current vertex
                    var distance2 = 0.0;
                    if (currentVertexDistance.IsPracticallySame(distance))
                    {
                        if (previousVertexDistance < distance - ts.SameTolerance)
                        {
                            distance2 = distance - ts.SameTolerance;
                        }
                        else
                        {
                            //Take the average if the function above did not work.
                            distance2 = (previousVertexDistance + currentVertexDistance / 2);
                        }
                    }
                    else
                    {
                        //There was a significant enough gap betwwen points to use the exact distance
                        distance2 = distance;
                    }

                    var cuttingPlane = new Flat(distance2, direction);
                    List<List<Edge>> outputEdgeLoops;
                    var inputEdgeLoops = new List<List<Edge>>();
                    var current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, inputEdgeLoops);

                    //Get a list of 2D paths from the 3D loops
                    //Get 2D projections does not reorder list if the cutting plane direction is negative
                    //So we need to do this ourselves. 
                    double[,] backTransform;
                    crossSection.AddRange(current3DLoops.Select(loop => MiscFunctions.Get2DProjectionPointsReorderingIfNecessary(loop, direction, out backTransform, ts.SameTolerance)));

                    return crossSection;
                }
                foreach (var edge in vertex.Edges)
                {
                    //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                    //and the second removes it from the list.
                    if (edgeListDictionary.ContainsKey(edge.IndexInList))
                    {
                        edgeListDictionary.Remove(edge.IndexInList);
                    }
                    else
                    {
                        edgeListDictionary.Add(edge.IndexInList, edge);
                    }
                }
                //Update the previous distance of the vertex checked
                previousVertexDistance = currentVertexDistance;
            }
            return null; //The function should return from the if statement inside
        }
        #endregion

        #region Local Classes
        /// <summary>
        /// The Decomposition Data Class used to store information from A Directional Decomposition.
        /// 
        /// </summary>
        public class DecompositionData
        {
            /// <summary>
            /// A list of the paths that make up the slice of the solid at this distance along this direction
            /// </summary>
            public List<List<Point>> Paths;

            /// <summary>
            /// The distance along this direction
            /// </summary>
            public double DistanceAlongDirection;

           /// <summary>
           /// The Decomposition Data Class used to store information from A Directional Decomposition
           /// </summary>
           /// <param name="paths"></param>
           /// <param name="distanceAlongDirection"></param>
           public DecompositionData(IEnumerable<List<Point>> paths, double distanceAlongDirection)
            {
                Paths = new List<List<Point>>(paths);
                DistanceAlongDirection = distanceAlongDirection;
            }
        }

        /// <summary>
        /// A data group for linking the 2D path, 3D path, and edge loop of cross section polygons.
        /// </summary>
        public class PolygonDataGroup
        {
            /// <summary>
            /// The 2D list of points that define this polygon in the cross section
            /// </summary>
            public List<Point> Path;

            /// <summary>
            /// The edge loop used to define the 3D path
            /// </summary>
            public List<Edge> EdgeLoop;

            /// <summary>
            /// The Index of the path in its cross section.
            /// </summary>
            public int IndexInCrossSection;

            /// <summary>
            /// The area of this loop
            /// </summary>
            public double Area { get; set; }

            /// <summary>
            /// Gets or sets the index of the segment that this loop and path belong to.
            /// </summary>
            public int SegmentIndex { get; set; }

            /// <summary>
            /// A data group for linking the 2D path and edge loop of cross section polygons.
            /// </summary>
            /// <param name="path"></param>
            /// <param name="edgeLoop"></param>
            /// <param name="indexInCrossSection"></param>
            public PolygonDataGroup(List<Point> path, List<Edge> edgeLoop, int indexInCrossSection)
            {
                Path = path;
                EdgeLoop = edgeLoop;
                IndexInCrossSection = indexInCrossSection;
            }

        }

        /// <summary>
        /// The SegmentationData Class used to store information from A Directional Segmentation Decomposition.
        /// It is the same as DecompositionData, except that it stores the 3D information as well.
        /// </summary>
        public class SegmentationData
        {
            /// <summary>
            /// A list of polygon data groups that makes up the cross section at this distance
            /// </summary>
            public List<PolygonDataGroup> CrossSectionData;

            /// <summary>
            /// The distance along this direction
            /// </summary>
            public double DistanceAlongDirection;

            /// <summary>
            /// The Segmentation Data Class used to store information from A Directional Segmented Decomposition
            /// </summary>
            /// <param name="paths"></param>
            /// <param name="distanceAlongDirection"></param>
            /// <param name="edgeLoops"></param>
            public SegmentationData(List<List<Point>> paths,
                List<List<Edge>> edgeLoops, double distanceAlongDirection)
            {
                CrossSectionData = new List<PolygonDataGroup>();
                for (var i = 0; i < paths.Count(); i++)
                {
                    CrossSectionData.Add(new PolygonDataGroup(paths[i], edgeLoops[i], i));
                }
                DistanceAlongDirection = distanceAlongDirection;
            }
        }
        #endregion

        /// <summary>
        /// Returns the Directional Segments found from decomposing a solid along a given direction. This data is used in other methods.
        /// </summary>
        /// <param name="ts"></param>
        /// <param name="direction"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        public static List<DirectionalSegment> UniformDirectionalSegmentation(TessellatedSolid ts, double[] direction,
            double stepSize)
        {
            var outputData = new List<SegmentationData>();

            List<Vertex> bottomVertices, topVertices;
            var length = MinimumEnclosure.GetLengthAndExtremeVertices(direction, ts.Vertices,
                out bottomVertices, out topVertices);

            //Set step size to an even increment over the entire length of the solid
            stepSize = length / Math.Round(length / stepSize + 1);

            //make the minimum step size 1/10 of the length.
            if (length < 10 * stepSize)
            {
                stepSize = length / 10;
            }

            //Choose whichever min offset is smaller
            var minOffset = Math.Min(Math.Sqrt(ts.SameTolerance), stepSize / 1000);

            //First, sort the vertices along the given axis. Duplicate distances are not important because they
            //will all be handled at the same step/distance.
            List<Tuple<Vertex, double>> sortedVertices;
            List<int[]> duplicateRanges;
            MiscFunctions.SortAlongDirection(new[] { direction }, ts.Vertices, out sortedVertices, out duplicateRanges);
            //Create a distance lookup dictionary based on the vertex indices
            var vertexDistanceLookup = sortedVertices.ToDictionary(element => element.Item1.IndexInList, element => element.Item2);

            //This is a list of all the current edges, those edges which are cut at the current distance along the axis. 
            //Each edge has an IndexInList, which is used as the dictionary Key.
            var edgeListDictionary = new Dictionary<int, Edge>();

            //This is a list of edges that is set in the GetLoops function. This list of edges is used in conjunction with 
            //outputEdgeLoops to limit the calls to the main GetLoops function, since the loops have not changed if the edgeListDictionary
            //has not changed.
            var inputEdgeLoops = new List<List<Edge>>();

            //A list of all the segments that have been started, but not finished.
            //Current segments can include partially started segments
            var currentSegments = new Dictionary<int, DirectionalSegment>();

            //A list of all the directional segments.
            var allDirectionalSegments = new Dictionary<int, DirectionalSegment>();

            var firstDistance = sortedVertices.First().Item2;
            var furthestDistance = sortedVertices.Last().Item2;
            var distanceAlongAxis = firstDistance;
            var currentVertexIndex = 0;
            var segmentIndex = 0;

            while (distanceAlongAxis < furthestDistance - stepSize)
            {
                //This is the current distance along the axis. It will move forward by the step size during each iteration.
                distanceAlongAxis += stepSize;

                //inPlaneEdges is a list of edges that are added to the edge list and removed in the same step.
                //This means that they are basically in the current plane. This list will be reset every time we take another step.
                //This list works in conjunction with the newlyAddedEdges list.
                var inPlaneEdges = new List<Edge>();

                //inStepVertices is a hashset of all the vertices that were considered in the current step.
                var inStepVertices = new HashSet<Vertex>();

                //inStepEdges is a hashset of all the edges that started in the current step. It is not the same as the 
                //edgeListDictionary because it ignores edges from prior steps.
                var inStepEdges = new HashSet<Edge>();

                //Update vertex/edge list up until distanceAlongAxis
                for (var i = currentVertexIndex; i < sortedVertices.Count; i++)
                {
                    //Update the current vertex index so that this vertex is not visited again
                    //unless it causes the break ( > distanceAlongAxis), then it will start the 
                    //the next iteration.
                    currentVertexIndex = i;
                    var element = sortedVertices[i];
                    var vertex = element.Item1;
                    var vertexDistanceAlong = element.Item2;
                    //If a vertex is too close to the current distance, move it forward by the min offset.
                    //Update the edge list with this vertex.
                    if (vertexDistanceAlong.IsPracticallySame(distanceAlongAxis, minOffset))
                    {
                        //Move the distance enough so that this vertex is now less than 
                        distanceAlongAxis = vertexDistanceAlong + minOffset * 1.1;
                        //if (vertexDistanceAlong.IsPracticallySame(distanceAlongAxis, minOffset))
                        //{
                        //    throw new Exception("Error in implementation. Need to move the distance further");
                        //}
                    }
                    //Else, Break after we get to a vertex that is further than the distance along axis
                    if (vertexDistanceAlong > distanceAlongAxis)
                    {
                        //consider this vertex again next iteration
                        break;
                    }

                    //Else, it is less than the distance along. Add the vertex to the inStepVertices and update the edge list.
                    inStepVertices.Add(vertex);

                    //Update the edge dictionary that is used to determine the 3D loops.
                    foreach (var edge in vertex.Edges)
                    {
                        //Reset the input edge loops since we have added an edge
                        inputEdgeLoops = new List<List<Edge>>();

                        //Every edge has only two vertices. So the first sorted vertex adds the edge to this list
                        //and the second removes it from the list.
                        if (edgeListDictionary.ContainsKey(edge.IndexInList))
                        {
                            edgeListDictionary.Remove(edge.IndexInList);

                            //If the edge being removed had also been added during this same step, add it to the inPlaneEdges.
                            if (inStepEdges.Contains(edge))
                            {
                                inPlaneEdges.Add(edge);
                            }
                        }
                        else
                        {
                            edgeListDictionary.Add(edge.IndexInList, edge);
                            inStepEdges.Add(edge);
                        }
                    }
                }

                //Get the decomposition data (cross sections) for this distance.
                //Before doing this, check to make sure that the minor shifts in the distance in the for loop above 
                //Did not move the distance beyond the furthest distance
                if (distanceAlongAxis <= furthestDistance && edgeListDictionary.Any())
                {
                    //The inputEdgeLoops is a reference, since it will be updated in the the method.
                    var decompositionData = GetSegmentationData(distanceAlongAxis, direction, edgeListDictionary,
                        ref inputEdgeLoops, minOffset);
                    if (decompositionData != null) outputData.Add(decompositionData);
                }

                //Regardless of whether we reached the end of the part, we will need to update the segments.
                while (currentSegments.Any())
                {
                    //Get and remove the first segment from the currentSegments dictionary.
                    var segment = currentSegments.First().Value;
                    currentSegments.Remove(segment.Index);

                    //Initialize a new list of segments
                    var connectedSegments = new HashSet<DirectionalSegment>();

                    //The temp edges are those that are in the current step, but do not continue further along the search direction
                    //These are all in-plane edges, or at least, very nearly in-plane.
                    var inStepSegmentEdges = new HashSet<Edge>();

                    //A list of all the edges that were finished during this step, not including the inStepSegmentEdges.
                    var finishedEdges = new HashSet<Edge>();

                    //The inStepSegmentVertices are those Vertices that are in the current step for the current segment.
                    //Most of these will also have been in segment.NextVertices,
                    //but in the case of larger flat surfaces (where we add multiple in-plane edges), 
                    //they might not have been contained in segment.NextVertices.
                    var allInStepSegmentVertices = new HashSet<Vertex>();
                    var inStepSegmentVertexSet = new Stack<Vertex>();


                    //Get the in-step vertices for the current segment that are contained in inStepVertices.
                    //This is only a part of the vertices that will be added to the list
                    foreach (var vertex in inStepVertices)
                    {
                        //If the vertex is contained in next vertices, add it to the "In-step" lists and 
                        //remove it from "NextVertices"
                        if (segment.NextVertices.Contains(vertex))
                        {
                            allInStepSegmentVertices.Add(vertex);
                            inStepSegmentVertexSet.Push(vertex);
                            segment.NextVertices.Remove(vertex);
                            segment.ReferenceVertices.Add(vertex);

                            //This vertex belongs to the current segment, so update the reference index.
                            vertex.ReferenceIndex = segment.Index;
                        }
                    }

                    while (inStepSegmentVertexSet.Any())
                    {
                        var vertex = inStepSegmentVertexSet.Pop();

                        foreach (var edge in vertex.Edges)
                        {

                            var otherVertex = edge.OtherVertex(vertex);
                            var otherVertexDistance = vertexDistanceLookup[otherVertex.IndexInList];

                            //Check to see where the otherVertex is located. There are 3 options:
                            //1) It is in the current step.
                            //2) It is after the current step.
                            //3) It is prior to the current step.

                            //Case 1: It is in the current step
                            if (inStepVertices.Contains(otherVertex))
                            {
                                //Add the vertex to the vertex set if it has not already been considered
                                if (otherVertex.ReferenceIndex != segment.Index)
                                {
                                    inStepSegmentVertexSet.Push(otherVertex);

                                    //This vertex belongs to the current segment, so update the reference index.
                                    allInStepSegmentVertices.Add(otherVertex);
                                    otherVertex.ReferenceIndex = segment.Index;
                                }
                                //Else, it already has the correct segment index as is being considered in the stack

                                //Since the current vertex is in the current step, if the other vertex is also in the current step, 
                                //Then the edge is in the current step.
                                if (inStepSegmentEdges.Contains(edge)) continue; //it is already in the edge list, which means so is the vertex.
                                inStepSegmentEdges.Add(edge);
                            }
                            //Case 2: It is after the current step.
                            //Add the edge and the otherVertex to the segment's "Next" lists if they do not already contain it.
                            else if (otherVertexDistance > distanceAlongAxis)
                            {
                                if (segment.CurrentEdges.Contains(edge)) throw new Exception("This edge could not have been already added, since it will only be visited once.");
                                segment.CurrentEdges.Add(edge); //This edge is not finished.

                                if (segment.NextVertices.Contains(otherVertex)) continue; //The edge was new, but the vertex was already in the list.
                                segment.NextVertices.Add(otherVertex);
                            }
                            //Case 3: The otherVertex is prior to the current step.
                            else
                            {
                                //The edge must point back to a prior segment, so the edge is finished
                                finishedEdges.Add(edge);

                                //Check which segment it points back to
                                //If the otherVertex belongs to the current segment, continue.
                                //It cannot connect to another segment, since reference vertices will only ever
                                //belong to one current segment.
                                if (segment.ReferenceVertices.Contains(otherVertex))
                                {
                                    edge.ArbitraryReferenceIndex = segment.Index;
                                    continue;
                                }

                                //Else, check the current segments.
                                //We are concerned with reference vertices, not NextVertices.
                                //This is because next vertices may tell connection, but too early.
                                //Also, if the next vertex is in-step, it will be connected to the current segment
                                //temporarily (by its ReferenceIndex) and the search will continue, eventually
                                //wrapping around the finished edges of the other segments. 
                                var foundSegment = false;
                                foreach (var otherSegment in currentSegments.Values)
                                {
                                    if (otherSegment.ReferenceVertices.Contains(otherVertex)) 
                                    {
                                        if (!connectedSegments.Contains(otherSegment))
                                        {
                                            connectedSegments.Add(otherSegment);
                                        }
                                        foundSegment = true;
                                        edge.ArbitraryReferenceIndex = otherSegment.Index;
                                        break; //It cannot belong to multiple segments.
                                    }
                                }
                                if (!foundSegment)
                                {
                                    throw new Exception("Segment not found, when looking for edge ownership");
                                }
                            }
                        }
                    }

                    //We have finished getting all the vertices and edges necessary to update the segment
                    //Update the current segment and all connected segments.
                    if (connectedSegments.Any())
                    {
                        //For connected segments and the current segment (which is connected), 
                        //close them properly and remove them from the current segment's list.
                        segment.IsFinished = true;

                        //Add the finished edges to the reference edges of whichever segment they belong to.
                        //The finished edgs are not truly complete in these segments, since the in-step
                        //vertices will actually belong to the new segment, but this will allow for the segment's
                        //cross sections to be defined with the set of edges for its entire length.
                        foreach (var edge in finishedEdges)
                        {
                            allDirectionalSegments[edge.ArbitraryReferenceIndex].ReferenceEdges.Add(edge);
                        }
                        foreach (var otherSegment in connectedSegments)
                        {
                            otherSegment.IsFinished = true;
                            currentSegments.Remove(otherSegment.Index);
                        }
                        connectedSegments.Add(segment); //We didn't add this earlier, since it had already been removed from currentSegments.

                        //Also, create a new segment that starts from these completed segments. 
                        //Don't add it to the current segments, yet.
                        //finishedEdges && inStepSegmentEdges => newSegment.ReferenceEdges
                        //allInStepSegmentVertices => newSegment.ReferenceVertices
                        //nextVertices => newSegment.NextVertices
                        //currentEdges => newSegment.CurrentEdges
                        var referenceEdges = new List<Edge>(finishedEdges);
                        referenceEdges.AddRange(inStepSegmentEdges);
                        var newSegment = new DirectionalSegment(segmentIndex, referenceEdges, 
                            allInStepSegmentVertices, segment.CurrentEdges, segment.NextVertices, connectedSegments.ToList());
                        allDirectionalSegments.Add(segmentIndex, newSegment);
                        segmentIndex++;
                    }
                    //Else, update the current segement
                    else
                    {
                        //Next vertices and next edges are already up=to-date
                        //The reference vertices and edges do need to be updated.
                        foreach (var edge in finishedEdges)
                        {
                            //Only add the finished 
                            segment.ReferenceEdges.Add(edge);
                        }
                        foreach (var vertex in allInStepSegmentVertices)
                        {
                            segment.ReferenceVertices.Add(vertex);
                        }
                    }
                }

                //Reset the dictionary of current segments
                currentSegments = new Dictionary<int, DirectionalSegment>();
                foreach (var segment in allDirectionalSegments.Values)
                {
                    if (!segment.IsFinished) currentSegments.Add(segment.Index, segment);
                    segment.CurrentPolygonDataGroups = new List<PolygonDataGroup>();
                }

                //Now that the segments have been updated, we have additional cases to check using the 2D paths
                //example: Blind holes and a branching segment have not been captured up to this point.
                //First, we need to connect each path (polygonDataSet stores the path and the edge loop) to its segment
                foreach (var polygonDataSet in outputData.Last().CrossSectionData)
                {
                    var i = 0;
                    polygonDataSet.SegmentIndex = -1;
                    while (polygonDataSet.SegmentIndex == -1 && i < 5)
                    {
                        var edge = polygonDataSet.EdgeLoop[i];
                        foreach (var currentSegment in currentSegments.Values)
                        {
                            if (currentSegment.CurrentEdges.Contains(edge))
                            {
                                polygonDataSet.SegmentIndex = currentSegment.Index;
                                currentSegment.CurrentPolygonDataGroups.Add(polygonDataSet);
                                break;
                            }
                        }
                        i++;
                    }
                    if (polygonDataSet.SegmentIndex == -1)
                    {
                        if (polygonDataSet.Area > 0) Debug.WriteLine("Did not find the edges. Why not?");
                        else
                        {
                            //The polygonDataSet is the start of a blind hole. We need to determine which positive polygon it fits inside.
                            foreach (var currentSegment in currentSegments.Values)
                            {
                                var paths = new List<List<Point>>();
                                foreach (var polygonDataGroup in currentSegment.CurrentPolygonDataGroups)
                                {
                                    paths.Add(polygonDataGroup.Path);
                                }

                                //IF the intersection results in any overlap, then it belongs to this segment.
                                //As a hole, it cannot belong to multiple segments and cannot split or merge segments.
                                //Note: you cannot just check if a point from the dataSet is inside the positive paths, 
                                //since it the blind hole could be nested inside positive/negative pairings. (ex: a hollow rod 
                                //down the middle of a larger hollow tube. In this case, the hollow rod is a differnt segment).
                                var result = PolygonOperations.Intersection(paths, polygonDataSet.Path);
                                if (result != null && result.Any())
                                {
                                    polygonDataSet.SegmentIndex = currentSegment.Index;
                                    currentSegment.CurrentPolygonDataGroups.Add(polygonDataSet);
                                }  
                            }
                        }
                    }
                }



                //ToDo: Create new segements from any unused vertices

            }

            //Add the first and last cross sections. 
            //Note, these may not be great fits if step size is large
            //outputData.Insert(0, new DecompositionData(outputData.First().Paths, firstDistance));
            //outputData.Add(new DecompositionData(outputData.Last().Paths, furthestDistance));

            return null;
        }

        private static SegmentationData GetSegmentationData(double distanceAlongAxis, double[] direction, Dictionary<int, Edge> edgeListDictionary, ref List<List<Edge>> inputEdgeLoops, double minOffset)
        {
            //Make the slice
            var counter = 0;
            var successfull = false;
            var cuttingPlane = new Flat(distanceAlongAxis, direction);
            do
            {
                try
                {
                    List<List<Edge>> outputEdgeLoops;
                    var current3DLoops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops,
                        inputEdgeLoops);

                    //Use the same output edge loops for outer while loop, since the edge list does not change.
                    //If there is an error, it will occur before this.
                    inputEdgeLoops = outputEdgeLoops;

                    //Get the area of this layer
                    var area = current3DLoops.Sum(p => MiscFunctions.AreaOf3DPolygon(p, direction));
                    if (area < 0)
                    {
                        Debug.WriteLine(
                            "Area for a cross section in UniformDirectionalDecomposition was negative. This means there was an issue with the polygon ordering");
                    }

                    double[,] backTransform;
                    //Get a list of 2D paths from the 3D loops
                    var currentPaths =
                        current3DLoops.Select(
                            cp =>
                                MiscFunctions.Get2DProjectionPointsReorderingIfNecessary(cp, direction,
                                    out backTransform));

                    successfull = true; //Irrelevant, since we are returning now.

                    //Add the data to the output
                    return new SegmentationData(currentPaths.ToList(), outputEdgeLoops, distanceAlongAxis);
                }
                catch
                {
                    counter++;
                    distanceAlongAxis += minOffset;
                }
            } while (!successfull && counter < 4);

            Debug.WriteLine("Slice at this distance was unsuccessful, even with multiple minimum offsets.");
            return null;
        }

        /// <summary>
        /// A directional segment is one of the pieces that a part is naturally divided into along a given direction.
        /// It is a collection of cross sections that are overlapping and faces that are connected.
        /// A blind hole inside a solid, is also part of the segment.
        /// A segment never may include multiple positive polygons, as long as they are attached with edge/face wrapping.
        /// </summary>
        public class DirectionalSegment
        {
            private bool _isFinished;

            /// <summary>
            /// Gets or sets whether the directional segment is finished collecting all its cross sections and face references.
            /// </summary>
            public bool IsFinished
            {
                get { return _isFinished; }
                set
                {
                    _isFinished = value;
                    if (_isFinished)
                    {
                        //NextVertices and Current Edges are irrelevant at this point.
                        NextVertices = null;
                        CurrentEdges = null;
                    }
                }
            }

            /// <summary>
            /// Gets or sets whether the directional segment is larger than one step size. If is was just started, it will not be
            /// fully started until the end of the current step. This is because it may merge with another Directional Segment.
            /// </summary>
            public bool IsFullyStarted { get; set; }

            /// <summary>
            /// A list of all the vertices that correspond to this segment. Some vertices may belong to multiple segments.
            /// </summary>
            public HashSet<Vertex> ReferenceVertices;

            /// <summary>
            /// A list of all the edges that correspond to this segment. Some edges may belong to multiple segments.
            /// </summary>
            public HashSet<Edge> ReferenceEdges;

            /// <summary>
            /// A list of all the edges that are currently being used for the decomposition. The plane is currently cutting through them.
            /// This is a companion to NextVertices.
            /// </summary>
            public HashSet<Edge> CurrentEdges;

            /// <summary>
            /// A list of all the vertices that we are looking into, but that are not yet assured to be part of this segment.
            /// </summary>
            public HashSet<Vertex> NextVertices;

            /// <summary>
            /// A list of all the faces that correspond to this segment. Some faces may be partly in this segment and partly in another.
            /// </summary>
            public List<PolygonalFace> ReferenceFaces;

            /// <summary>
            /// A dictionary that contains all the cross sections corresponding to this segment. The integer is the step number (distance) along
            /// the search direction.
            /// </summary>
            public Dictionary<int, IList<List<Point>>> CrossSectionPathDictionary;

            /// <summary>
            /// The direction by which the directional segment was defined. The cross sections and face references will be ordered
            /// along this direction.
            /// </summary>
            public double[] ForwardDirection;

            /// <summary>
            /// A list of the current polygon data groups, which make up the current segment cross section
            /// </summary>
            public List<PolygonDataGroup> CurrentPolygonDataGroups { get; set; }

            /// <summary>
            /// A list of all the directional segments that are adjoined to this segment along the forward direction.
            /// </summary>
            public List<DirectionalSegment> ForwardAdjoinedDirectionalSegment { get; set; }

            /// <summary>
            /// A list of all the directional segments that are adjoined to this segment along the rearward direction.
            /// </summary>
            public List<DirectionalSegment> RearwardAdjoinedDirectionalSegment { get; set; }

            /// <summary>
            /// The index that is unique to this directional segment. The segments are ordered based on whichever
            /// is started first along the search direction.
            /// </summary>
            public int Index;

            /// <summary>
            /// Starts a directional segment given a vertex. 
            /// </summary>
            /// <param name="vertex"></param>
            /// <param name="index"></param>
            /// <param name="direction"></param>
            /// <param name="edges"></param>
            public DirectionalSegment(Vertex vertex, int index, double[] direction, IEnumerable<Edge> edges  )
            {
                Index = index;
                CrossSectionPathDictionary = new Dictionary<int, IList<List<Point>>>();
                ForwardAdjoinedDirectionalSegment = new List<DirectionalSegment>();
                RearwardAdjoinedDirectionalSegment = new List<DirectionalSegment>();
                ReferenceEdges = new HashSet<Edge>();
                IsFinished = false;
                //This segment was just started, so it will not be  fully started until the end of the current step. 
                //This is because it may merge with another Directional Segment.
                IsFullyStarted = false;
                ForwardDirection = direction;

                ReferenceFaces = new List<PolygonalFace>();
                ReferenceVertices = new HashSet<Vertex>() {vertex};

                CurrentEdges = new HashSet<Edge>(edges);
                NextVertices = new HashSet<Vertex>();
            }

            /// <summary>
            /// Starts a directional segment based on prior connected directional segments
            /// </summary>
            /// <param name="index"></param>
            /// <param name="referenceEdges"></param>
            /// <param name="referenceVertices"></param>
            /// <param name="currentEdges"></param>
            /// <param name="nextVertices"></param>
            /// <param name="parentDirectionalSegments"></param>
            public DirectionalSegment(int index, IEnumerable<Edge> referenceEdges, IEnumerable<Vertex> referenceVertices, 
                IEnumerable<Edge> currentEdges, IEnumerable<Vertex> nextVertices, 
                List<DirectionalSegment> parentDirectionalSegments )
            {
                Index = index;
                CrossSectionPathDictionary = new Dictionary<int, IList<List<Point>>>();
                ForwardAdjoinedDirectionalSegment = new List<DirectionalSegment>();
                RearwardAdjoinedDirectionalSegment = new List<DirectionalSegment>(parentDirectionalSegments);
                ReferenceEdges = new HashSet<Edge>(referenceEdges);
                ReferenceVertices = new HashSet<Vertex>(referenceVertices);

                //This segment has a jumpstart, since it is built from other segments
                IsFullyStarted = true;
                IsFinished = false;

                //This segment has the same forward direction as its parents
                ForwardDirection = parentDirectionalSegments.First().ForwardDirection;

                ReferenceFaces = new List<PolygonalFace>();
                CurrentEdges = new HashSet<Edge>(currentEdges);
                NextVertices = new HashSet<Vertex>(nextVertices);

                //This segment has a jumpstart, since it is built from other segments
                IsFullyStarted = true;
            }

            /// <summary>
            /// Gets the Vertex Paths for the cross sections, given the cross sections, forward direction, and distances. 
            /// </summary>
            /// <param name="direction"></param>
            /// <param name="distanceDictionary"></param>
            /// <returns></returns>
            public List<List<List<double[]>>> GetVertexPaths(double[] direction, Dictionary<int, double> distanceDictionary)
            {
                //ToDo: Move this function over to TVGL
                //return SubVolume.GetBlankCrossSections(direction, CrossSectionPathDictionary, distanceDictionary);
                throw new NotImplementedException();
            }

            /// <summary>
            /// Displays the vertex paths for debugging.
            /// </summary>
            /// <param name="direction"></param>
            /// <param name="distanceDictionary"></param>
            /// <exception cref="NotImplementedException"></exception>
            public void Display(double[] direction, Dictionary<int, double> distanceDictionary)
            {
                //Presenter.ShowVertexPaths(GetVertexPaths(direction, distanceDictionary));
                throw new NotImplementedException();
            }

            public void Update(HashSet<Vertex> allInStepSegmentVertices, HashSet<Edge> nextEdges, 
                HashSet<Vertex> nextVertices, List<DirectionalSegment> connectedSegments, 
                ref Dictionary<int, DirectionalSegment> allDirectionalSegments)
            {
                //If there are no connected segments, simply update the vertices and edges and continue.

            }


            //public void Add(int crossSectionIndex, List<List<Point>> paths, ref Dictionary<int, DirectionalSegment> segments)
            //{
            //    //1) Adding another group at the same crossSection index, closes current trace.
            //    //And builds two new traces. If a third group is added, it will fall into the reasoning below (2).
            //    //2) Adding another trace to a closed trace, results in new trace.
            //    if (IsClosed)
            //    {
            //        var traceIndex = traces.Count;
            //        var newTrace = new Trace(crossSectionIndex, paths, traces.Count);
            //        traces.Add(traceIndex, newTrace);

            //        //Connect the traces 
            //        this.ForwardAdjoinedTraces.Add(newTrace);
            //        newTrace.RearwardAdjoinedTraces.Add(this);
            //    }
            //    else if (FurthestDecompositionDataIndex == crossSectionIndex)
            //    {
            //        // Debug.WriteLine("This trace already contains a cross section at this distance");

            //        //Create two new traces 
            //        var traceIndex = traces.Count;
            //        var newTrace1 = new Trace(FurthestDecompositionDataIndex, FurthestCrossSection, traceIndex);
            //        traces.Add(traceIndex, newTrace1);
            //        traceIndex++;
            //        var newTrace2 = new Trace(crossSectionIndex, paths, traceIndex);
            //        traces.Add(traceIndex, newTrace2);
            //        IsClosed = true;

            //        //Connect the traces 
            //        this.ForwardAdjoinedTraces.Add(newTrace1);
            //        newTrace1.RearwardAdjoinedTraces.Add(this);

            //        this.ForwardAdjoinedTraces.Add(newTrace2);
            //        newTrace2.RearwardAdjoinedTraces.Add(this);

            //        //Remove the prior cross section, then update the prior information
            //        CrossSectionPathDictionary.Remove(FurthestDecompositionDataIndex);
            //        TraceDecompositionDataIndices.Remove(FurthestDecompositionDataIndex);
            //        FurthestDecompositionDataIndex--;
            //        if (CrossSectionPathDictionary.Count < 1)
            //        {
            //            //This trace has been collapsed (contains no cross sections)
            //            //Remove it from the list of traces
            //            traces.Remove(Index);
            //        }

            //    }
            //    else
            //    {
            //        TraceDecompositionDataIndices.Add(crossSectionIndex);
            //        CrossSectionPathDictionary.Add(crossSectionIndex, paths);
            //        FurthestDecompositionDataIndex = crossSectionIndex;
            //    }
            //}
        }

        #region Private Supporting Methods
        /// <summary>
        ///     Crosses the sectional area.
        /// </summary>
        /// <param name="edgeListDictionary">The edge list dictionary.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <param name="outputEdgeLoops">The output edge loops.</param>
        /// <param name="intputEdgeLoops">The intput edge loops.</param>
        /// <param name="ignoreNegativeSpace">if set to <c>true</c> [ignore negative space].</param>
        /// <returns>System.Double.</returns>
        /// <exception cref="Exception">Loop did not complete</exception>
        private static double CrossSectionalArea(Dictionary<int, Edge> edgeListDictionary, Flat cuttingPlane,
            out List<List<Edge>> outputEdgeLoops, List<List<Edge>> intputEdgeLoops, bool ignoreNegativeSpace = false)
        {
            var loops = GetLoops(edgeListDictionary, cuttingPlane, out outputEdgeLoops, intputEdgeLoops);
            var totalArea = 0.0;
            foreach (var loop in loops)
            {
                //The area function returns negative values for negative loops and positive values for positive loops
                var area = MiscFunctions.AreaOf3DPolygon(loop, cuttingPlane.Normal);
                if (ignoreNegativeSpace && Math.Sign(area) < 0) continue;
                totalArea += area;
            }
            return totalArea;
        }

        private static List<List<Vertex>>   GetLoops(Dictionary<int, Edge> edgeListDictionary, Flat cuttingPlane,
            out List<List<Edge>> outputEdgeLoops, List<List<Edge>> intputEdgeLoops)
        {
            var edgeLoops = new List<List<Edge>>();
            var loops = new List<List<Vertex>>();
            if (intputEdgeLoops.Any())
            {
                edgeLoops = intputEdgeLoops; //Note that edge loops should all be ordered correctly
                foreach (var edgeLoop in edgeLoops)
                {
                    var loop = new List<Vertex>();
                    foreach (var edge in edgeLoop)
                    {
                        var vertex = MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal, cuttingPlane.DistanceToOrigin,
                        edge.To, edge.From);
                        vertex.Edges.Add(edge);
                        loop.Add(vertex);
                    }
                    loops.Add(loop);
                }
            }
            else
            {
                //Build an edge list that we can modify, without ruining the original
                //After comparing hashset versus dictionary (with known keys)
                //Hashset was slighlty faster during creation and enumeration, 
                //but even more slighlty slower at removing. Overall, Hashset 
                //was about 17% faster than a dictionary.
                var edges = new List<Edge>(edgeListDictionary.Values);
                var unusedEdges = new HashSet<Edge>(edges);
                foreach (var startEdge in edges)
                {
                    if (!unusedEdges.Contains(startEdge)) continue;
                    unusedEdges.Remove(startEdge);;
                    var loop = new List<Vertex>();
                    var intersectVertex = MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                        cuttingPlane.DistanceToOrigin, startEdge.To, startEdge.From);
                    loop.Add(intersectVertex);
                    var edgeLoop = new List<Edge> { startEdge };
                    var startFace = startEdge.OwnedFace;
                    var currentFace = startFace;
                    var previousFace = startFace; //This will be set again before its used.
                    var endFace = startEdge.OtherFace;
                    var nextEdgeFound = false;
                    Edge nextEdge = null;
                    var correctDirection = 0.0;
                    var reverseDirection = 0.0;
                    do
                    {
                        //Get the next edge
                        foreach (var edge in currentFace.Edges)
                        {
                            if (!unusedEdges.Contains(edge)) continue;
                            if (edge.OtherFace == currentFace)
                            {
                                previousFace = edge.OtherFace;
                                currentFace = edge.OwnedFace;
                                nextEdgeFound = true;
                                nextEdge = edge;
                                break;
                            }
                            if (edge.OwnedFace == currentFace)
                            {
                                previousFace = edge.OwnedFace;
                                currentFace = edge.OtherFace;
                                nextEdgeFound = true;
                                nextEdge = edge;
                                break;
                            }
                        }
                        if (nextEdgeFound)
                        {
                            //For the first set of edges, check to make sure this list is going in the proper direction
                            intersectVertex = MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                                cuttingPlane.DistanceToOrigin, nextEdge.To, nextEdge.From);
                            //Add the edge as a reference for the vertex, so we can get the faces later
                            intersectVertex.Edges.Add(nextEdge);
                            var vector = intersectVertex.Position.subtract(loop.Last().Position);
                            //Use the previous face, since that is the one that contains both of the edges that are in use.
                            var dot = cuttingPlane.Normal.crossProduct(previousFace.Normal).dotProduct(vector);
                            loop.Add(intersectVertex);
                            edgeLoop.Add(nextEdge);
                            unusedEdges.Remove(nextEdge);
                            //Note that removing at an index is FASTER than removing a object.
                            if (Math.Sign(dot) >= 0) correctDirection += dot;
                            else reverseDirection += (-dot);
                        }
                        else throw new Exception("Loop did not complete");
                    } while (currentFace != endFace);

                    if (reverseDirection > 1 && correctDirection > 1) throw new Exception("Area Decomp Loop Finding needs additional work.");
                    if (reverseDirection > correctDirection)
                    {
                        loop.Reverse();
                        edgeLoop.Reverse();
                    }
                    loops.Add(loop);
                    edgeLoops.Add(edgeLoop);
                }
            }
            outputEdgeLoops = edgeLoops;
            return loops;
        }

        /// <summary>
        ///     Convexes the hull2 d area.
        /// </summary>
        /// <param name="edgeList">The edge list.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <returns>System.Double.</returns>
        private static double ConvexHull2DArea(Dictionary<int, Edge> edgeList, Flat cuttingPlane)
        {
            //Don't bother with loops. Just get all the intercept vertices, project to 2d and run 2dConvexHull
            var vertices =
                edgeList.Select(
                    edge =>
                        MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                            cuttingPlane.DistanceToOrigin, edge.Value.To, edge.Value.From));
            var points = MiscFunctions.Get2DProjectionPoints(vertices.ToArray(), cuttingPlane.Normal, true);
            return MinimumEnclosure.ConvexHull2DArea(MinimumEnclosure.ConvexHull2D(points));
        }

        /// <summary>
        ///     Boundings the rectangle area.
        /// </summary>
        /// <param name="edgeList">The edge list.</param>
        /// <param name="cuttingPlane">The cutting plane.</param>
        /// <returns>System.Double.</returns>
        private static double BoundingRectangleArea(Dictionary<int, Edge> edgeList, Flat cuttingPlane)
        {
            //Don't bother with loops. Just get all the intercept vertices, project to 2d and run 2dConvexHull
            var vertices =
                edgeList.Select(
                    edge =>
                        MiscFunctions.PointOnPlaneFromIntersectingLine(cuttingPlane.Normal,
                            cuttingPlane.DistanceToOrigin, edge.Value.To, edge.Value.From));
            var points = MiscFunctions.Get2DProjectionPoints(vertices.ToArray(), cuttingPlane.Normal, true);
            var boundingRectangle = MinimumEnclosure.BoundingRectangle(points, false);
            return boundingRectangle.Area;
        }
        #endregion
    }
}