import Foundation

public enum Templates {
    /// The JSON Schema shipped with the package.
    public static var schema: String {
        guard let url = Bundle.module.url(forResource: "mockiexam.schema", withExtension: "json"),
              let s = try? String(contentsOf: url, encoding: .utf8) else { return "{}" }
        return s
    }

    /// A minimal valid question of each kind, for agents and humans to copy.
    public static func question(_ kind: QuestionKind) -> String {
        switch kind {
        case .single: """
            {
              "id": "dns-port", "kind": "single", "topic": "Ports",
              "prompt": "Which port does DNS use?",
              "choices": [{ "id": "a", "text": "53" }, { "id": "b", "text": "67" }, { "id": "c", "text": "443" }],
              "answer": "a",
              "explanation": "DNS uses 53/UDP, and TCP for large responses."
            }
            """
        case .multiple: """
            {
              "id": "private-ranges", "kind": "multiple", "topic": "Addressing",
              "prompt": "Which are RFC 1918 ranges? (Choose two.)",
              "choices": [{ "id": "a", "text": "10.0.0.0/8" }, { "id": "b", "text": "172.32.0.0/12" }, { "id": "c", "text": "192.168.0.0/16" }],
              "answer": ["a", "c"]
            }
            """
        case .trueFalse: """
            { "id": "udp-reliable", "kind": "trueFalse", "prompt": "UDP guarantees delivery.", "answer": false }
            """
        case .yesNo: """
            {
              "id": "vnet-statements", "kind": "yesNo", "topic": "Networking",
              "prompt": "For each statement, select Yes if it is true. Otherwise, select No.",
              "statements": [
                { "id": "s1", "text": "Subnets in one virtual network can communicate by default.", "answer": true },
                { "id": "s2", "text": "A virtual network can span regions.", "answer": false }
              ]
            }
            """
        case .ordering: """
            {
              "id": "dhcp-order", "kind": "ordering",
              "prompt": "Move the DHCP messages to the answer area in the order they are sent.",
              "choices": [{ "id": "d", "text": "Discover" }, { "id": "o", "text": "Offer" }, { "id": "r", "text": "Request" }, { "id": "a", "text": "Ack" }, { "id": "x", "text": "Release" }],
              "answer": ["d", "o", "r", "a"]
            }
            """
        case .matching: """
            {
              "id": "port-protocol", "kind": "matching", "partialCredit": true,
              "prompt": "Drag each protocol to its default port.",
              "items": [{ "id": "ssh", "text": "SSH" }, { "id": "rdp", "text": "RDP" }, { "id": "ftp", "text": "FTP" }],
              "targets": [{ "id": "22", "text": "22" }, { "id": "3389", "text": "3389" }, { "id": "21", "text": "21" }],
              "answer": { "ssh": "22", "rdp": "3389", "ftp": "21" }
            }
            """
        case .dropdowns: """
            {
              "id": "acl-hotarea", "kind": "dropdowns",
              "prompt": "Complete the ACL so only HTTPS from 10.0.0.0/24 is allowed.",
              "rows": [
                { "id": "action", "label": "Action", "options": ["permit", "deny"], "answer": "permit" },
                { "id": "port", "label": "Destination port", "options": ["80", "443", "8080"], "answer": "443" }
              ]
            }
            """
        case .fillIn: """
            {
              "id": "show-routes", "kind": "fillIn", "topic": "IOS",
              "prompt": "Enter the command that displays the IPv4 routing table.",
              "blanks": [{ "id": "cmd", "accept": ["show ip route", "sh ip route"] }]
            }
            """
        case .hotspot: """
            {
              "id": "nat-device", "kind": "hotspot",
              "prompt": "Click the device that performs NAT for the LAN.",
              "image": "images/topology.png",
              "regions": [
                { "id": "router", "label": "Edge router", "x": 0.40, "y": 0.05, "width": 0.20, "height": 0.20 },
                { "id": "switch", "label": "Switch", "x": 0.40, "y": 0.45, "width": 0.20, "height": 0.15 }
              ],
              "answer": ["router"]
            }
            """
        }
    }

    public static var exam: String {
        let questions = QuestionKind.allCases.filter { $0 != .hotspot }.map { question($0) }
            .map { $0.split(separator: "\n").map { "    " + $0 }.joined(separator: "\n") }
            .joined(separator: ",\n")
        return """
        {
          "formatVersion": 1,
          "id": "my-exam",
          "title": "My Exam",
          "passPercent": 70,
          "timeLimitMinutes": 30,
          "questions": [
        \(questions)
          ]
        }
        """
    }
}
