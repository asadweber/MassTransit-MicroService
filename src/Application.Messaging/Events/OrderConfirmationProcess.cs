using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Messaging.Events
{
    public enum OrderConfirmationProcess
    {
        Email = 1,
        SMS = 2,
        Paci = 3,
        Notification = 4
    }
}
