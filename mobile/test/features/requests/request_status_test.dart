import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('only AgentProcessing refreshes on its own for now', () {
    expect(RequestStatuses.refreshing, {RequestStatuses.agentProcessing});
    expect(RequestStatuses.needsRefresh(RequestStatuses.agentProcessing), isTrue);
    for (final status in [
      RequestStatuses.submitted,
      RequestStatuses.pendingApproval,
      RequestStatuses.approved,
      RequestStatuses.agentFailed,
      RequestStatuses.cancelled,
    ]) {
      expect(RequestStatuses.needsRefresh(status), isFalse, reason: status);
    }
    expect(RequestStatuses.refreshInterval, const Duration(seconds: 3));
  });
}
