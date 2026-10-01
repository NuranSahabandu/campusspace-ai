import 'dart:convert';

import 'package:campusspace_mobile/features/notifications/status_notices.dart';
import 'package:campusspace_mobile/features/requests/models.dart';
import 'package:campusspace_mobile/features/requests/request_status.dart';
import 'package:flutter_test/flutter_test.dart';

import '../../fixtures/proposals.dart';
import '../../fixtures/requests.dart';

RequestSnapshot snap(int id, String status, {bool byOfficer = false, bool bySystem = false}) => RequestSnapshot(
    id: id, purpose: 'Purpose $id', status: status, cancelledByOfficer: byOfficer, closedBySystem: bySystem);

RequestDetail detail(String json) => RequestDetail.fromJson(jsonDecode(json) as Map<String, dynamic>);

void main() {
  group('noticeFor', () {
    const s = RequestStatuses.pendingApproval;
    final cases = <(String, String, NoticeKind?, String)>[
      (s, RequestStatuses.approved, NoticeKind.approved, 'approved'),
      (s, RequestStatuses.rejected, NoticeKind.rejected, 'rejected by an officer'),
      (s, RequestStatuses.revisionRequested, NoticeKind.revision, 'revise seen as RevisionRequested'),
      (s, RequestStatuses.agentProcessing, NoticeKind.revision, 'revise seen as AgentProcessing'),
      (RequestStatuses.agentProcessing, RequestStatuses.agentFailed, NoticeKind.failed, 'agent failed'),
      (RequestStatuses.agentProcessing, s, null, 'a new proposal is not notified'),
      (RequestStatuses.submitted, RequestStatuses.agentProcessing, null, 'agent started'),
      (RequestStatuses.agentFailed, RequestStatuses.agentProcessing, null, 'officer retry'),
      (RequestStatuses.approved, RequestStatuses.completed, null, 'completed'),
      (s, RequestStatuses.cancelled, null, "the owner's own cancel"),
      (s, s, null, 'unchanged'),
    ];
    for (final (from, to, kind, name) in cases) {
      test(name, () => expect(noticeFor(snap(1, from), snap(1, to)), kind));
    }

    test('closed automatically when the system rejected it', () {
      expect(noticeFor(snap(1, s), snap(1, RequestStatuses.rejected, bySystem: true)), NoticeKind.closed);
    });

    test('cancelled by an officer', () {
      expect(noticeFor(snap(1, RequestStatuses.approved), snap(1, RequestStatuses.cancelled, byOfficer: true)),
          NoticeKind.cancelledByOfficer);
    });
  });

  group('RequestSnapshot.fromDetail (captured API responses)', () {
    test('an officer rejection has an actor', () {
      final r = RequestSnapshot.fromDetail(detail(rejectedDetailJson));
      expect((r.status, r.closedBySystem), (RequestStatuses.rejected, false));
    });

    test('a system rejection has none', () {
      expect(RequestSnapshot.fromDetail(detail(closedAutomaticallyJson)).closedBySystem, isTrue);
    });

    test('an officer cancellation keeps the flag', () {
      expect(RequestSnapshot.fromDetail(detail(officerCancelledJson)).cancelledByOfficer, isTrue);
    });
  });

  group('StatusBaseline', () {
    test('the first update is the baseline and notifies nothing', () {
      final b = StatusBaseline();
      expect(b.update([snap(1, RequestStatuses.approved), snap(2, RequestStatuses.agentFailed)]), isEmpty);
      expect(b.hasBaseline, isTrue);
    });

    test('a change notifies once, a refresh with the same data never again', () {
      final b = StatusBaseline()..update([snap(1, RequestStatuses.pendingApproval)]);
      expect(b.update([snap(1, RequestStatuses.approved)]),
          [const StatusNotice(requestId: 1, kind: NoticeKind.approved, purpose: 'Purpose 1')]);
      expect(b.update([snap(1, RequestStatuses.approved)]), isEmpty);
    });

    test('a request first seen after the baseline is recorded silently', () {
      final b = StatusBaseline()..update([]);
      expect(b.update([snap(5, RequestStatuses.agentProcessing)]), isEmpty);
      expect(b.update([snap(5, RequestStatuses.agentFailed)]).single.kind, NoticeKind.failed);
    });

    test('several changes in one update each notify', () {
      final b = StatusBaseline()
        ..update([snap(1, RequestStatuses.pendingApproval), snap(2, RequestStatuses.pendingApproval)]);
      final notices = b.update([snap(1, RequestStatuses.approved), snap(2, RequestStatuses.agentProcessing)]);
      expect(notices.map((n) => n.kind), [NoticeKind.approved, NoticeKind.revision]);
    });

    test('clear (logout) drops the baseline, so the next update notifies nothing', () {
      final b = StatusBaseline()..update([snap(1, RequestStatuses.pendingApproval)]);
      b.clear();
      expect(b.hasBaseline, isFalse);
      expect(b.ids, isEmpty);
      expect(b.update([snap(1, RequestStatuses.approved)]), isEmpty);
    });

    test('forget stops tracking a request', () {
      final b = StatusBaseline()..update([snap(1, RequestStatuses.approved)]);
      b.forget(1);
      expect(b.ids, isEmpty);
    });
  });

  group('text', () {
    test('safePurpose strips control characters and collapses whitespace', () {
      expect(safePurpose(' Robotics\n\tclub\u0007  meetup\u0085 '), 'Robotics club meetup');
    });

    test('safePurpose cuts to 40 characters', () {
      final cut = safePurpose('A' * 60);
      expect(cut.runes.length, purposeMaxLength);
      expect(cut, endsWith('…'));
      expect(safePurpose('B' * 40), 'B' * 40);
    });

    test('every template is short plain text with the purpose and no id', () {
      for (final kind in NoticeKind.values) {
        final t = noticeText(StatusNotice(requestId: 4242, kind: kind, purpose: 'Robotics meetup'));
        expect(t.title, isNotEmpty);
        expect(t.body, contains('"Robotics meetup"'));
        expect('${t.title}${t.body}', isNot(contains('4242')));
      }
    });
  });
}
