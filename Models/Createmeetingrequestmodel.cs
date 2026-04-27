using System;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace VMeetTool.Models
{
    public class CreateMeetingRequestModel
    {
        [Required(ErrorMessage = "Host user ID is required.")]
        public int host_user_id { get; set; }

        [Required(ErrorMessage = "Title is required.")]
        [StringLength(200, ErrorMessage = "Title cannot exceed 200 characters.")]
        public string title { get; set; }

        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
        public string description { get; set; }

        // Accept format: "dd-MM-yyyy HH:mm"  e.g. "25-04-2026 10:00"
        [Required(ErrorMessage = "Start time is required.")]
        public string start_time { get; set; }

        [Required(ErrorMessage = "End time is required.")]
        public string end_time { get; set; }

        // Hidden from Swagger and JSON binding
        [JsonIgnore]
        public DateTime ParsedStartTime =>
            DateTime.ParseExact(start_time, "dd-MM-yyyy HH:mm",
                                System.Globalization.CultureInfo.InvariantCulture);

        [JsonIgnore]
        public DateTime ParsedEndTime =>
            DateTime.ParseExact(end_time, "dd-MM-yyyy HH:mm",
                                System.Globalization.CultureInfo.InvariantCulture);
    }
}