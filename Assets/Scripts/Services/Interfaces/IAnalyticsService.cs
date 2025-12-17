using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Analytics;
using Firebase.Extensions;

namespace LoGa.LudoEngine.Services
{
    public interface IAnalyticsService : IService
    {
        void TrackEvent(string eventName);
        void TrackEventWithData(string eventName, Dictionary<string, object> parameters);
        void SetAnalyticsConsent(bool consent);
        void SetUserProperty(string propertyName, string value);
        void SetFeedbackCode(string feedbackCode);
    }
}
