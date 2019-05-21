using Autodesk.AutoCAD.ApplicationServices.Core;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace DWGValidation
{
    public class Commands
    {
        [CommandMethod("RUNVALIDATION")]
        public static void RunValidation()
        {
            Database db = Application.DocumentManager.MdiActiveDocument.Database;
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            dynamic response = new JObject();
            response.values = new JArray();
            response.areas = new JArray();

            ed.WriteMessage("\nPreparing loop...");

            using (Transaction trans = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord mSpace = trans.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId entId in mSpace)
                {
                    MText text = trans.GetObject(entId, OpenMode.ForRead) as MText;
                    if (text == null) continue;

                    ed.WriteMessage("\nHas text above?");
                    try
                    {
                        MText textAbove = FindTextAbove(mSpace, trans, text);
                        if (textAbove != null)
                        {
                            dynamic keyValue = new JObject();
                            keyValue.name = new JObject();
                            keyValue.name.text = text.Text;
                            keyValue.name.handle = text.Handle.ToString();
                            keyValue.value = new JObject();
                            keyValue.value.text = textAbove.Text;
                            keyValue.value.handle = textAbove.Handle.ToString();

                            response.values.Add(keyValue);
                        }
                    }
                    catch (System.Exception ex) { ed.WriteMessage(ex.Message); }

                    try
                    {
                        ed.WriteMessage("\nHas hatch on the left?");
                        Hatch hatchLeft = FindHatchToLeft(mSpace, trans, text);
                        if (hatchLeft != null)
                        {
                            dynamic keyValue = new JObject();
                            keyValue.name = new JObject();
                            keyValue.name.text = text.Text;
                            keyValue.name.handle = text.Handle.ToString();

                            ed.WriteMessage("\nFind similar hatches");
                            List<Hatch> otherHatches = SumHatchArea(mSpace, trans, hatchLeft);
                            keyValue.hatches = new JArray();
                            foreach (Hatch h in otherHatches)
                            {
                                dynamic hatchInfo = new JObject();
                                hatchInfo.handle = h.Handle.ToString();
                                try { hatchInfo.area = h.Area; }
                                catch { }

                                keyValue.hatches.Add(hatchInfo);
                            }

                            response.areas.Add(keyValue);
                        }
                    }
                    catch (System.Exception ex) { ed.WriteMessage(ex.Message); }
                }
            }

            try
            {
                ed.WriteMessage("\nSaving file...");
                StreamWriter writer = new StreamWriter("results.json");
                writer.WriteLine(response.ToString());
                writer.Close();
            }
            catch (System.Exception ex) { ed.WriteMessage(ex.Message); }

        }

        public static MText FindTextAbove(BlockTableRecord mSpace, Transaction trans, MText text)
        {
            LineSegment3d current = new LineSegment3d(text.Bounds.Value.MinPoint, text.Bounds.Value.MaxPoint);
            Line3d lineUp = new Line3d(current.MidPoint, Vector3d.YAxis);
            double distance = double.MaxValue;
            MText ret = null;

            foreach (ObjectId entId in mSpace)
            {
                MText thisText = trans.GetObject(entId, OpenMode.ForRead) as MText;
                if (thisText == null) continue;

                LineSegment3d thisTextLine = new LineSegment3d(thisText.Bounds.Value.MinPoint, thisText.Bounds.Value.MaxPoint);
                Point3d[] intersectionPoints = lineUp.IntersectWith(thisTextLine);
                if (intersectionPoints == null || intersectionPoints.Length != 1) continue;
                double thisDistance = current.MidPoint.DistanceTo(intersectionPoints[0]);
                if (thisDistance < distance && !thisText.ObjectId.Equals(text.ObjectId) && current.MidPoint.Y < intersectionPoints[0].Y)
                {
                    distance = thisDistance;
                    ret = thisText;
                }
            }
            return ret;
        }

        public static Hatch FindHatchToLeft(BlockTableRecord mSpace, Transaction trans, MText text)
        {
            LineSegment3d current = new LineSegment3d(text.Bounds.Value.MinPoint, text.Bounds.Value.MaxPoint);
            Line3d lineUp = new Line3d(current.MidPoint, Vector3d.XAxis);
            double distance = double.MaxValue;
            Hatch ret = null;

            foreach (ObjectId entId in mSpace)
            {
                Hatch thisHatch = trans.GetObject(entId, OpenMode.ForRead) as Hatch;
                if (thisHatch == null) continue;

                LineSegment3d thisHatchLine = new LineSegment3d(thisHatch.Bounds.Value.MinPoint, thisHatch.Bounds.Value.MaxPoint);
                Point3d[] intersectionPoints = lineUp.IntersectWith(thisHatchLine);
                if (intersectionPoints == null || intersectionPoints.Length != 1) continue;
                double thisDistance = current.MidPoint.DistanceTo(intersectionPoints[0]);
                if (thisDistance < distance && !thisHatch.ObjectId.Equals(text.ObjectId) && current.MidPoint.X > intersectionPoints[0].X)
                {
                    distance = thisDistance;
                    ret = thisHatch;
                }
            }
            return ret;
        }

        public static List<Hatch> SumHatchArea(BlockTableRecord mSpace, Transaction trans, Hatch hatch)
        {
            List<Hatch> ret = new List<Hatch>();
            foreach (ObjectId entId in mSpace)
            {
                Hatch thisHatch = trans.GetObject(entId, OpenMode.ForRead) as Hatch;
                if (thisHatch == null) continue;
                if (thisHatch.ObjectId.Equals(hatch.ObjectId)) continue;

                if (thisHatch.PatternName == hatch.PatternName && thisHatch.Layer == hatch.Layer)
                    ret.Add(thisHatch);
            }
            return ret;
        }

        public static Line FindClosestLine(BlockTableRecord mSpace, Transaction trans, MText text)
        {
            double distance = double.MaxValue;
            Line closestLine = null;
            foreach (ObjectId entId in mSpace)
            {
                Line line = trans.GetObject(entId, OpenMode.ForRead) as Line;
                Point3d point = line.GetClosestPointTo(text.Location, false);
                double d = point.DistanceTo(text.Location);
                if (d < distance && (line.StartPoint.Y > text.Location.Y) && (d < (text.Bounds.Value.MaxPoint.Y - text.Bounds.Value.MinPoint.Y)))
                {
                    d = distance;
                    closestLine = line;
                }
            }
            return closestLine;
        }
    }
}
