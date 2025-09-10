"# NavisPlugin" 
static public Point3D GetClosestGridIntersection()
      {
          GridIntersection oGridIntersection = null; ;

          //check that selection is valid
          if (!Autodesk.Navisworks.Api.Application.ActiveDocument.CurrentSelection.IsEmpty)
          {
              //Get bounding box of the selection
              BoundingBox3D bb3d =
                 Autodesk.Navisworks.Api.Application.ActiveDocument.CurrentSelection.SelectedItems.BoundingBox();

              GridSystem oGS = Autodesk.Navisworks.Api.Application.ActiveDocument.Grids.ActiveSystem;

              //get the closest grid intersection point
              oGridIntersection = oGS.ClosestIntersection(bb3d.Center);
          }

          //return the vector
          return oGridIntersection.Position;
      }
