import { LabGate } from "./lab-gate";

// The Affiliate Lab's pages. Only an Organization that has the Lab is shown them.
export default function LabLayout({ children }: LayoutProps<"/lab">) {
  return <LabGate>{children}</LabGate>;
}
