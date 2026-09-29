import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('the agent statuses refresh every 3 s, a pending proposal every 15 s, anything else never', () {
    const threeSeconds = Duration(seconds: 3);
    expect(RequestStatuses.refreshIntervalFor(RequestStatuses.agentProcessing), threeSeconds);
    expect(RequestStatuses.refreshIntervalFor(RequestStatuses.revisionRequested), threeSeconds);
    expect(RequestStatuses.refreshIntervalFor(RequestStatuses.pendingApproval), const Duration(seconds: 15));
    for (final status in [
      RequestStatuses.submitted,
      RequestStatuses.approved,
      RequestStatuses.completed,
      RequestStatuses.agentFailed,
      RequestStatuses.rejected,
      RequestStatuses.cancelled,
    ]) {
      expect(RequestStatuses.refreshIntervalFor(status), isNull, reason: status);
    }
  });

  test('a list refreshes at its shortest interval', () {
    expect(
      RequestStatuses.shortestRefreshInterval([RequestStatuses.pendingApproval, RequestStatuses.agentProcessing]),
      const Duration(seconds: 3),
    );
    expect(RequestStatuses.shortestRefreshInterval([RequestStatuses.approved, RequestStatuses.pendingApproval]),
        const Duration(seconds: 15));
    expect(RequestStatuses.shortestRefreshInterval([RequestStatuses.approved, RequestStatuses.rejected]), isNull);
    expect(RequestStatuses.shortestRefreshInterval([]), isNull);
  });
}
