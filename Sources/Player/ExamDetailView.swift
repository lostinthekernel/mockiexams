import MockiCore
import SwiftData
import SwiftUI

struct ExamDetailView: View {
    let exam: Exam
    let onStart: (SessionOptions, _ fullScreen: Bool, _ style: ExamStyle) -> Void

    @Query private var attempts: [Attempt]
    @State private var options = SessionOptions()
    @State private var drawCount = 0
    @State private var topics: Set<String> = []
    @AppStorage("fullScreenExams") private var fullScreen = true
    @AppStorage("examStyle") private var style: ExamStyle = .modern

    init(exam: Exam, onStart: @escaping (SessionOptions, Bool, ExamStyle) -> Void) {
        self.exam = exam
        self.onStart = onStart
        let examID = exam.id
        _attempts = Query(filter: #Predicate<Attempt> { $0.examID == examID }, sort: \.date, order: .reverse)
    }

    private var asked: [Question] { exam.examQuestions }
    private var pool: Int { asked.count { topics.isEmpty || topics.contains($0.topic) } }

    private var lastMissed: [String] {
        let known = Set(asked.map(\.id))
        return (attempts.first?.missedQuestionIDs ?? []).filter(known.contains)
    }

    private var kindSummary: String {
        QuestionKind.allCases.compactMap { kind in
            let n = asked.count { $0.kind == kind }
            return n > 0 ? "\(n) \(kind.label.lowercased())" : nil
        }.joined(separator: ", ")
    }

    var body: some View {
        Form {
            Section {
                if let summary = exam.summary { Text(summary).foregroundStyle(.secondary) }
                LabeledContent("Questions", value: "\(asked.count)\(exam.sections.isEmpty ? "" : " in \(exam.sections.count) sections")")
                LabeledContent("Types", value: kindSummary)
                LabeledContent("Pass mark", value: exam.scale.map { "\($0.pass) on a \($0.min)–\($0.max) scale" } ?? "\(exam.passPercent)%")
                LabeledContent("Time limit", value: exam.timeLimit.map(formatDuration) ?? "None")
                if !exam.allowBack { LabeledContent("Navigation", value: "No going back in exam simulation") }
                if !exam.caseStudies.isEmpty { LabeledContent("Case studies", value: "\(exam.caseStudies.count)") }
            } header: {
                Text(exam.title).font(.title2.bold()).foregroundStyle(.primary)
            }

            Section("Start") {
                Picker("Mode", selection: $options.mode) {
                    ForEach(SessionMode.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.segmented)
                Text(options.mode == .practice
                     ? "Check each answer as you go and see the explanation."
                     : "Like the real thing: \(exam.timeLimit == nil ? "untimed" : "timed"), graded at the end\(exam.allowBack ? "" : ", no going back").")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                if options.mode == .exam {
                    Picker("Exam screen", selection: $style) {
                        ForEach(ExamStyle.allCases) { Text($0.label).tag($0) }
                    }
                    Toggle("Full screen", isOn: $fullScreen)
                }
                Toggle("Shuffle question order", isOn: $options.shuffleQuestions)
                Toggle("Shuffle answer order", isOn: $options.shuffleChoices)
                Picker("Questions", selection: $drawCount) {
                    Text("All \(pool)").tag(0)
                    ForEach([10, 20, 30, 50, 65, 90].filter { $0 < pool }, id: \.self) { Text("Random \($0)").tag($0) }
                }
                if exam.topics.count > 1 {
                    LabeledContent("Topics") {
                        Menu(topics.isEmpty ? "All topics" : "\(topics.count) selected") {
                            Button("All topics") { topics = [] }
                            Divider()
                            ForEach(exam.topics, id: \.self) { topic in
                                Toggle(topic, isOn: Binding(
                                    get: { topics.contains(topic) },
                                    set: { if $0 { topics.insert(topic) } else { topics.remove(topic) } }))
                            }
                        }
                        .fixedSize()
                    }
                }
                HStack {
                    Button("Start") { start(nil) }
                        .keyboardShortcut(.defaultAction)
                    if !lastMissed.isEmpty {
                        Button("Retry \(lastMissed.count) Missed") { start(Set(lastMissed)) }
                    }
                }
            }

            let weak = attempts.weakTopics()
            if !weak.isEmpty {
                Section("Topic accuracy (all attempts)") {
                    ForEach(weak) { TopicRow(score: $0) }
                }
            }

            Section("History") {
                if attempts.isEmpty {
                    Text("No attempts yet.").foregroundStyle(.secondary)
                }
                ForEach(attempts) { attempt in
                    HStack {
                        Image(systemName: attempt.passed ? "checkmark.seal.fill" : "xmark.seal")
                            .foregroundStyle(attempt.passed ? .green : .orange)
                        Text(attempt.scaledScore.map(String.init) ?? "\(attempt.percent)%").monospacedDigit().frame(width: 44, alignment: .trailing)
                        Text(attempt.sessionMode.label).foregroundStyle(.secondary)
                        Text("\(attempt.questionCount) q").foregroundStyle(.secondary)
                        Spacer()
                        Text(formatDuration(attempt.duration)).monospacedDigit().foregroundStyle(.secondary)
                        Text(attempt.date, format: .dateTime.month().day().hour().minute())
                            .foregroundStyle(.secondary)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func start(_ ids: Set<String>?) {
        var o = options
        o.questionIDs = ids
        o.topics = topics.isEmpty ? nil : topics
        o.questionLimit = ids == nil && drawCount > 0 ? drawCount : nil
        o.seed = .random(in: .min ... .max)
        onStart(o, o.mode == .exam && fullScreen, style)
    }
}
