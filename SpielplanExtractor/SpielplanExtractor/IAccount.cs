using System.Threading.Tasks;

namespace SpielplanExtractor
{
    internal interface IAccount
    {
        Task SetUpAppointmentsAsync(Season season);
    }
}