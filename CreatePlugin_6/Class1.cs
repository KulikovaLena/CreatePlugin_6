using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreatePlugin_6
{
    [TransactionAttribute(TransactionMode.Manual)]
    public class CreationModel : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            Level level1 = GetLevel(doc, "Уровень 1");
            Level level2 = GetLevel(doc, "Уровень 2");

            Transaction transaction = new Transaction(doc, "Create");
            transaction.Start();

            List<Wall> walls = CreateWall(doc, level1.Id, 10, 5, 3);
            AddDoor(doc, level1, walls[0]);
            AddWindows(doc, level1, walls);
            //AddRoof(doc, level2, walls);
            AddExtrusionRoof(doc, level2, walls, 3);

            transaction.Commit();

            return Result.Succeeded;
        }

        private void AddExtrusionRoof(Document doc, Level level, List<Wall> walls, double heightWalls)
        {

            RoofType type = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .OfType<RoofType>()
                .Where(x => x.Name.Equals("Типовой - 400мм"))
                .Where(x => x.FamilyName.Equals("Базовая крыша"))
                .FirstOrDefault();

            double thickness = type.get_Parameter(BuiltInParameter.ROOF_ATTR_DEFAULT_THICKNESS_PARAM).AsDouble();

            CurveArray curveArray = new CurveArray();
            LocationCurve locationCurve = walls[1].Location as LocationCurve;

            XYZ zHeightWalls = new XYZ(0, 0, (UnitUtils.ConvertToInternalUnits(heightWalls, UnitTypeId.Meters))+ thickness);
            XYZ wallWidth = new XYZ(0, walls[0].Width/2, 0);
            XYZ p1 = locationCurve.Curve.GetEndPoint(0)+ zHeightWalls - wallWidth;
            XYZ p2 = locationCurve.Curve.GetEndPoint(1)+ zHeightWalls + wallWidth;
            XYZ z = new XYZ(0, 0, UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters));
            XYZ p3 = (p1+p2)/2 + z;
            curveArray.Append(Line.CreateBound(p1, p3));
            curveArray.Append(Line.CreateBound(p3, p2));

            LocationCurve extrusionEnd = walls[0].Location as LocationCurve;
            double extrusionEndLength = extrusionEnd.Curve.Length+walls[0].Width;

            ReferencePlane plane = doc.Create.NewReferencePlane(new XYZ(0, 0, 0), new XYZ(0, 0, 20), new XYZ(0, 20, 0), doc.ActiveView);

            doc.Create.NewExtrusionRoof(curveArray, plane, level, type, -extrusionEndLength / 2, extrusionEndLength- extrusionEndLength / 2);
            
        }

        private void AddRoof(Document doc, Level level2, List<Wall> walls)
        {
            RoofType roofType = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .OfType<RoofType>()
                .Where(x => x.Name.Equals("Типовой - 400мм"))
                .Where(x => x.FamilyName.Equals("Базовая крыша"))
                .FirstOrDefault();

            double wallWidth = walls[0].Width;
            double dt = wallWidth / 2;

            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dt, -dt, 0));
            points.Add(new XYZ(dt, -dt, 0));
            points.Add(new XYZ(dt, dt, 0));
            points.Add(new XYZ(-dt, dt, 0));
            points.Add(new XYZ(-dt, -dt, 0));

            Application application = doc.Application;
            CurveArray footprint = application.Create.NewCurveArray();

            for (int i = 0; i < 4; i++)
            {
                LocationCurve curve = walls[i].Location as LocationCurve;
                XYZ p1 = curve.Curve.GetEndPoint(0);
                XYZ p2 = curve.Curve.GetEndPoint(1);
                Line line = Line.CreateBound(p1 + points[i], p2 + points[i + 1]);
                footprint.Append(line);
            }
            ModelCurveArray footPrintToModelCurveMapping = new ModelCurveArray();
            FootPrintRoof footprintRoof = doc.Create.NewFootPrintRoof(footprint, level2, roofType, out footPrintToModelCurveMapping);

            //ModelCurveArrayIterator iterator = footPrintToModelCurveMapping.ForwardIterator();
            //iterator.Reset();
            //while (iterator.MoveNext())
            //{
            //    ModelCurve modelCurve = iterator.Current as ModelCurve;
            //    footprintRoof.set_DefinesSlope(modelCurve, true);
            //    footprintRoof.set_SlopeAngle(modelCurve, 0.5);
            //}

            foreach (ModelCurve m in footPrintToModelCurveMapping)
            {
                footprintRoof.set_DefinesSlope(m, true);
                footprintRoof.set_SlopeAngle(m, 0.5);
            }
        }

        private void AddWindows(Document doc, Level level1, List<Wall> walls)
        {
            FamilySymbol windowType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0610 x 1220 мм"))
                .Where(x => x.FamilyName.Equals("Фиксированные"))
                .FirstOrDefault();

            for (int i = 0; i < walls.Count(); i++)
            {
                LocationCurve hostCurve = walls[i].Location as LocationCurve;
                XYZ point1 = hostCurve.Curve.GetEndPoint(0);
                XYZ point2 = hostCurve.Curve.GetEndPoint(1);
                XYZ z = new XYZ(0, 0, UnitUtils.ConvertToInternalUnits(0.9, UnitTypeId.Meters));
                XYZ point = (point1 + point2) / 2 + z;

                if (!windowType.IsActive)
                    windowType.Activate();

                IList<ElementId> inserts = walls[i].FindInserts(true, true, true, true);
                if (0 == inserts.Count)
                {
                    doc.Create.NewFamilyInstance(point, windowType, walls[i], level1, StructuralType.NonStructural);
                }
            }
        }

        private void AddDoor(Document doc, Level level1, Wall wall)
        {
            FamilySymbol doorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfType<FamilySymbol>()
                .Where(x => x.Name.Equals("0915 x 2134 мм"))
                .Where(x => x.FamilyName.Equals("Одиночные-Щитовые"))
                .FirstOrDefault();

            LocationCurve hostCurve = wall.Location as LocationCurve;
            XYZ point1 = hostCurve.Curve.GetEndPoint(0);
            XYZ point2 = hostCurve.Curve.GetEndPoint(1);
            XYZ point = (point1 + point2) / 2;

            if (!doorType.IsActive)
                doorType.Activate();

            doc.Create.NewFamilyInstance(point, doorType, wall, level1, StructuralType.NonStructural);
        }

        public Level GetLevel(Document doc, string levelName)
        {
            Level level1 = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .OfType<Level>()
                .ToList()
                .Where(x => x.Name.Equals(levelName))
                .FirstOrDefault();

            return level1;
        }

        public List<Wall> CreateWall(Document doc, ElementId levelId, double width, double depth, double height)
        {
            double wd = UnitUtils.ConvertToInternalUnits(width, UnitTypeId.Meters);
            double dp = UnitUtils.ConvertToInternalUnits(depth, UnitTypeId.Meters);
            double hg = UnitUtils.ConvertToInternalUnits(height, UnitTypeId.Meters);

            double dx = wd / 2;
            double dy = dp / 2;

            List<XYZ> points = new List<XYZ>();
            points.Add(new XYZ(-dx, -dy, 0));
            points.Add(new XYZ(dx, -dy, 0));
            points.Add(new XYZ(dx, dy, 0));
            points.Add(new XYZ(-dx, dy, 0));
            points.Add(new XYZ(-dx, -dy, 0));

            WallType wall1 = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .OfType<WallType>()
                .ToList()
                .Where(x => x.Name.Equals("Наружный - Стена из кирпича с наружным слоем лицевого кирпича толщиной 380 мм и Кирпич фасадный - 250ммx65мм оштукатуренная 25 мм"))
                .FirstOrDefault();

            List<Wall> walls = new List<Wall>();

            for (int i = 0; i < 4; i++)
            {
                Line line = Line.CreateBound(points[i], points[i + 1]);
                Wall wall = Wall.Create(doc, line, wall1.Id, levelId, hg, 0, true, false);
                walls.Add(wall);
            }

            return walls;
        }
    }
}
