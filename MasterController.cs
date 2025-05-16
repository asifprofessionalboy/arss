using GFAS.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace GFAS.Controllers
{
    public class MasterController : Controller

    {
        private readonly TSUISLRFIDDBContext context;

        public MasterController(TSUISLRFIDDBContext context)
        {
            this.context = context;
        }
        public async Task<IActionResult> LocationMaster(Guid? id, AppLocationMaster locationMaster, int page = 1, string searchString = "")
        {

            var UserId = HttpContext.Request.Cookies["Session"];

            if (!string.IsNullOrEmpty(UserId))
            {
                if (UserId != "151514")
                {
                    return RedirectToAction("Login", "User");
                }


                ViewBag.Data = UserId;



                int pageSize = 5;
                var query = context.AppLocationMasters.AsQueryable();


                if (!string.IsNullOrEmpty(searchString))
                {
                    query = query.Where(p => p.WorkSite.Contains(searchString));
                }

                var data = query.ToList().OrderBy(loc => loc.WorkSite);






                var pagedData = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                var totalCount = data.Count();

                //ViewBag.SearchLcode = wsite;

                ViewBag.pList = pagedData;
                ViewBag.CurrentPage = page;

                ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                ViewBag.SearchString = searchString;

                if (id.HasValue)
                {
                    var model = await context.AppLocationMasters.FindAsync(id.Value);
                    if (model == null)
                    {
                        return NotFound();
                    }

                    return Json(new
                    {
                        id = model.Id,
                        worksite = model.WorkSite,
                        longitude = model.Longitude,
                        latitude = model.Latitude,
                        range = model.Range,
                        createdby = UserId,
                        createdon = model.CreatedOn
                    });
                }

                return View(new AppLocationMaster());
            }


            else
            {
                return RedirectToAction("Login", "User");
            }

        }

        [HttpPost]
        public async Task<IActionResult> LocationMaster([FromBody] LocationMasterViewModel model, Guid? Id)
        {


            var UserId = HttpContext.Request.Cookies["Session"];
            if (model == null || model.appLocations == null || !model.appLocations.Any())
            {
                return BadRequest("No location data received.");
            }


            if (string.IsNullOrEmpty(model.actionType))
            {
                return BadRequest("No action specified.");
            }

            if (model.actionType == "Submit")
            {
                if (!ModelState.IsValid)
                {
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            Console.WriteLine($"Key:{state.Key}, Error:{error.ErrorMessage}");
                        }
                    }
                }

                foreach (var appLocation in model.appLocations)
                {
                    var existingLocation = await context.AppLocationMasters.FindAsync(appLocation.Id);
                    appLocation.CreatedBy = UserId;
                    if (existingLocation != null)
                    {


                        context.Entry(existingLocation).CurrentValues.SetValues(appLocation);

                    }
                    else
                    {

                        await context.AppLocationMasters.AddAsync(appLocation);
                    }
                }


                await context.SaveChangesAsync();


                TempData["msg"] = "Location Saved Successfully!";
                return RedirectToAction("LocationMaster");
            }
            else if (model.actionType == "Delete")
            {
                foreach (var appLocation in model.appLocations)
                {

                    var existingLocation = await context.AppLocationMasters.FindAsync(appLocation.Id);
                    if (existingLocation != null)
                    {

                        context.AppLocationMasters.Remove(existingLocation);
                    }
                }


                await context.SaveChangesAsync();


                TempData["Dltmsg"] = "Location Deleted Successfully!";
                return RedirectToAction("LocationMaster");
            }

            return View(model);
        }


        public async Task<IActionResult> PositionMaster(Guid? id, AppPositionWorksite appPosition, int page = 1, string searchValue = "")
        {
            var UserId = HttpContext.Request.Cookies["Session"];

            if (!string.IsNullOrEmpty(UserId))
            {
                if (UserId != "151514")
                {
                    return RedirectToAction("Login", "User");
                }

                int pageSize = 5;
                var query = context.AppPositionWorksites.AsQueryable();


                var position = context.AppEmpPositions
                    .Where(e => e.Pno == searchValue)
                    .Select(e => e.Position)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(position.ToString()))
                {
                    query = query.Where(p => p.Position == position);
                }
                else
                {

                    ViewBag.ErrorMessage = "No Position found for this P.No.";
                }



                var pagedData = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();
                var totalCount = query.Count();

                ViewBag.pList = pagedData;
                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                ViewBag.SearchValue = searchValue;


                var WorksiteList = context.AppLocationMasters
                    .Select(x => new SelectListItem
                    {
                        Value = x.WorkSite,
                        Text = x.WorkSite
                    }).Distinct().ToList();

                ViewBag.WorksiteDDList = WorksiteList;

                var WorksiteList2 = context.AppEmpPositions
                    .Select(x => new SelectListItem
                    {
                        Value = x.Position.ToString(),
                        Text = x.Position.ToString()
                    }).ToList();

                ViewBag.PositionDDList = WorksiteList2;

                if (id.HasValue)
                {
                    var model = await context.AppPositionWorksites.FindAsync(id.Value);
                    if (model == null)
                    {
                        return NotFound();
                    }

                    return Json(new
                    {
                        id = model.Id,
                        position = model.Position,
                        worksite = model.Worksite,
                        createdby = UserId,
                        createdon = model.CreatedOn,
                    });
                }

                return View(new AppPositionWorksite());
            }
            else
            {
                return RedirectToAction("Login", "User");
            }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PositionMaster(AppPositionWorksite appPosition, string actionType)
        {
            if (string.IsNullOrEmpty(actionType))
            {
                return BadRequest("No action specified.");
            }

            var existingParameter = await context.AppPositionWorksites.FindAsync(appPosition.Id);


            if (actionType == "Submit")
            {
                if (!ModelState.IsValid)
                {
                    foreach (var state in ModelState)
                    {
                        foreach (var error in state.Value.Errors)
                        {
                            Console.WriteLine($"Key:{state.Key},Error:{error.ErrorMessage}");
                        }
                    }
                }


                if (ModelState.IsValid)
                {


                    if (existingParameter != null)
                    {

                        context.Entry(existingParameter).CurrentValues.SetValues(appPosition);
                        await context.SaveChangesAsync();
                        TempData["Updatedmsg"] = "Position Updated Successfully!";
                        return RedirectToAction("PositionMaster");
                    }
                    else
                    {


                        await context.AppPositionWorksites.AddAsync(appPosition);
                        await context.SaveChangesAsync();
                        TempData["msg"] = "Position Added Successfully!";
                        return RedirectToAction("PositionMaster");
                    }
                }
            }
            else if (actionType == "Delete")
            {
                if (existingParameter != null)
                {
                    context.AppPositionWorksites.Remove(existingParameter);
                    await context.SaveChangesAsync();
                    TempData["Dltmsg"] = "Position Deleted Successfully!";
                }
            }

            return RedirectToAction("PositionMaster");
        }
    }
}
