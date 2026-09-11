using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssessmentBL.DTOs.Quiz.Common
{
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = new List<T>(); // البيانات نفسها (الاختبارات)
        public int TotalCount { get; set; } // إجمالي عدد العناصر في قاعدة البيانات
        public int PageNumber { get; set; } // رقم الصفحة الحالية
        public int PageSize { get; set; } // عدد العناصر في كل صفحة
    }
}
