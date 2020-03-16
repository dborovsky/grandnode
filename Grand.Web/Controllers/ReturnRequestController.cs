﻿using Grand.Core;
using Grand.Core.Domain.Common;
using Grand.Core.Domain.Customers;
using Grand.Core.Domain.Orders;
using Grand.Services.Localization;
using Grand.Services.Orders;
using Grand.Web.Commands.Models.Orders;
using Grand.Web.Extensions;
using Grand.Web.Features.Models.Orders;
using Grand.Web.Interfaces;
using Grand.Web.Models.Orders;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Grand.Web.Controllers
{
    public partial class ReturnRequestController : BasePublicController
    {
        #region Fields

        private readonly IReturnRequestService _returnRequestService;
        private readonly IOrderService _orderService;
        private readonly IWorkContext _workContext;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly ILocalizationService _localizationService;
        private readonly IAddressViewModelService _addressViewModelService;
        private readonly IMediator _mediator;

        private readonly OrderSettings _orderSettings;
        #endregion

        #region Constructors

        public ReturnRequestController(
            IReturnRequestService returnRequestService,
            IOrderService orderService,
            IWorkContext workContext,
            IOrderProcessingService orderProcessingService,
            ILocalizationService localizationService,
            IAddressViewModelService addressViewModelService,
            IMediator mediator,
            OrderSettings orderSettings)
        {
            _returnRequestService = returnRequestService;
            _orderService = orderService;
            _workContext = workContext;
            _orderProcessingService = orderProcessingService;
            _localizationService = localizationService;
            _addressViewModelService = addressViewModelService;
            _mediator = mediator;
            _orderSettings = orderSettings;
        }

        #endregion

        #region Utilities

        protected async Task<Address> PrepareAddress(ReturnRequestModel model, IFormCollection form)
        {
            string pickupAddressId = form["pickup_address_id"];
            var address = new Address();
            if (_orderSettings.ReturnRequests_AllowToSpecifyPickupAddress)
            {
                if (!string.IsNullOrEmpty(pickupAddressId))
                {
                    address = _workContext.CurrentCustomer.Addresses.FirstOrDefault(a => a.Id == pickupAddressId);
                }
                else
                {
                    var customAttributes = await _addressViewModelService.ParseCustomAddressAttributes(form);
                    var customAttributeWarnings = await _addressViewModelService.GetAttributeWarnings(customAttributes);
                    foreach (var error in customAttributeWarnings)
                    {
                        ModelState.AddModelError("", error);
                    }
                    await TryUpdateModelAsync(model.NewAddress, "ReturnRequestNewAddress");
                    address = model.NewAddress.ToEntity();
                    model.NewAddressPreselected = true;
                    address.CustomAttributes = customAttributes;
                    address.CreatedOnUtc = DateTime.UtcNow;
                }
            }
            return address;
        }

        #endregion

        #region Methods

        public virtual async Task<IActionResult> CustomerReturnRequests()
        {
            if (!_workContext.CurrentCustomer.IsRegistered())
                return Challenge();

            var model = await _mediator.Send(new GetReturnRequests());

            return View(model);
        }

        public virtual async Task<IActionResult> ReturnRequest(string orderId, string errors = "")
        {
            var order = await _orderService.GetOrderById(orderId);
            if (order == null || order.Deleted || _workContext.CurrentCustomer.Id != order.CustomerId)
                return Challenge();

            if (!await _orderProcessingService.IsReturnRequestAllowed(order))
                return RedirectToRoute("HomePage");

            //var model = new ReturnRequestModel();
            var model = await _mediator.Send(new GetReturnRequest() { Order = order });
            model.Error = errors;
            return View(model);
        }

        [HttpPost, ActionName("ReturnRequest")]
        [AutoValidateAntiforgeryToken]
        public virtual async Task<IActionResult> ReturnRequestSubmit(string orderId, ReturnRequestModel model, IFormCollection form)
        {
            var order = await _orderService.GetOrderById(orderId);
            if (order == null || order.Deleted || _workContext.CurrentCustomer.Id != order.CustomerId)
                return Challenge();

            if (!await _orderProcessingService.IsReturnRequestAllowed(order))
                return RedirectToRoute("HomePage");

            ModelState.Clear();

            if (_orderSettings.ReturnRequests_AllowToSpecifyPickupDate && _orderSettings.ReturnRequests_PickupDateRequired && model.PickupDate == null)
            {
                ModelState.AddModelError("", _localizationService.GetResource("ReturnRequests.PickupDateRequired"));
            }

            var address = await PrepareAddress(model, form);

            if (!ModelState.IsValid && ModelState.ErrorCount > 0)
            {
                var returnmodel = await _mediator.Send(new GetReturnRequest() { Order = order });
                returnmodel.Error = string.Join(", ", ModelState.Keys.SelectMany(k => ModelState[k].Errors).Select(m => m.ErrorMessage).ToArray());
                returnmodel.Comments = model.Comments;
                returnmodel.PickupDate = model.PickupDate;
                returnmodel.NewAddressPreselected = model.NewAddressPreselected;
                returnmodel.NewAddress = model.NewAddress;
                return View(returnmodel);
            }
            else
            {
                var result = await _mediator.Send(new ReturnRequestSubmitCommand() { Address = address, Model = model, Form = form, Order = order });
                if (result.rr.ReturnNumber > 0)
                {
                    model.Result = string.Format(_localizationService.GetResource("ReturnRequests.Submitted"), result.rr.ReturnNumber, Url.Link("ReturnRequestDetails", new { returnRequestId = result.rr.Id }));
                    return View(result.model);
                }

                var returnmodel = await _mediator.Send(new GetReturnRequest() { Order = order });
                returnmodel.Error = result.model.Error;
                returnmodel.Comments = model.Comments;
                returnmodel.PickupDate = model.PickupDate;
                returnmodel.NewAddressPreselected = model.NewAddressPreselected;
                returnmodel.NewAddress = model.NewAddress;
                return View(returnmodel);
            }
            
        }

        public virtual async Task<IActionResult> ReturnRequestDetails(string returnRequestId)
        {
            var rr = await _returnRequestService.GetReturnRequestById(returnRequestId);
            if (rr == null || _workContext.CurrentCustomer.Id != rr.CustomerId)
                return Challenge();

            var order = await _orderService.GetOrderById(rr.OrderId);
            if (order == null || order.Deleted || _workContext.CurrentCustomer.Id != order.CustomerId)
                return Challenge();

            var model = await _mediator.Send(new GetReturnRequestDetails() { Order = order, ReturnRequest = rr });

            return View(model);
        }

        #endregion
    }
}
