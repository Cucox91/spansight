// NFR-2: monthly budget ≤ $50 with an alert at $40, armed with the very first deployment —
// the budget ships in the same template as the resources it guards.
targetScope = 'subscription'

@description('Monthly cap in USD (NFR-2).')
param amount int = 50

@description('Email that receives budget alerts.')
param contactEmail string

@description('First day of the current month, used as the budget period start.')
param startDate string = utcNow('yyyy-MM-01')

resource budget 'Microsoft.Consumption/budgets@2024-08-01' = {
  name: 'budget-spansight'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      // $40 actual (the NFR-2 alert line)
      actual80: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      // Early warning when the forecast crosses the cap itself
      forecast100: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [contactEmail]
      }
    }
  }
}
